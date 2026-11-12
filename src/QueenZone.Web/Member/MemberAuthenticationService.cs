using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

/// <summary>
/// Shared find-or-link-or-create logic for both the native Queenzone.org signup and every
/// external provider's first sign-in.
/// </summary>
/// <remarks>
/// Legacy USERS_T matching is by email only — the legacy PASSWORD column is never read.
/// New accounts do not auto-claim; members confirm the link from Account settings.
/// When several USERS_T rows share the same email, Settings offers a choice; silent login
/// backfill only runs when exactly one free match remains.
/// Existing unlinked accounts get that single-match silent backfill on later sign-in,
/// repairing accounts that missed create-time linking under the old model.
/// A new external provider is linked only when its email is verified and the existing
/// account confirms the link. A returning provider subject signs in without that check.
/// </remarks>
public sealed class MemberAuthenticationService(
    IMemberAccountRepository memberAccountRepository,
    LegacyAccountLinkService legacyLinks,
    TimeProvider timeProvider,
    IOptions<PasswordSignInLockoutOptions> passwordLockout)
{
    private readonly TimeProvider clock = timeProvider;
    private readonly PasswordSignInLockoutOptions lockout = passwordLockout.Value;

    private readonly PasswordHasher<MemberAccount> passwordHasher = new();

    public async Task<MemberAccountResult> RegisterAsync(string email, string password, string displayName, CancellationToken cancellationToken = default)
    {
        var existing = await memberAccountRepository.FindByEmailAsync(email, cancellationToken);
        if (existing is not null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Conflict, "An account with that email already exists.");
        }

        var account = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        };
        account.PasswordHash = passwordHasher.HashPassword(account, password);

        var created = await memberAccountRepository.CreateAsync(account, cancellationToken);
        return MemberAccountResult.Success(created);
    }

    public async Task<MemberAccountResult> SignInAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByEmailAsync(email, cancellationToken);
        if (account is null || account.PasswordHash is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.InvalidCredentials, InvalidPasswordSignInError);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var maxFailures = Math.Max(1, lockout.MaxFailures);
        var window = TimeSpan.FromMinutes(Math.Max(1, lockout.WindowMinutes));
        var failureCount = account.PasswordFailureCount;
        var windowStarted = account.PasswordFailureWindowStartedAt;
        if (windowStarted is null || now - windowStarted.Value >= window)
        {
            failureCount = 0;
            windowStarted = null;
        }

        var verification = passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        var locked = failureCount >= maxFailures;
        if (locked || verification == PasswordVerificationResult.Failed || account.IsSuspended)
        {
            if (!locked)
            {
                failureCount++;
                windowStarted ??= now;
                await memberAccountRepository.RecordPasswordFailureAsync(
                    account.Id,
                    failureCount,
                    windowStarted.Value,
                    cancellationToken);
            }

            return MemberAccountResult.Failure(MemberAccountOutcome.InvalidCredentials, InvalidPasswordSignInError);
        }

        var rehashedPassword = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? passwordHasher.HashPassword(account, password)
            : null;
        account = await legacyLinks.TryBackfillLegacyLinkAsync(account, cancellationToken);
        if (rehashedPassword is not null)
        {
            account.PasswordHash = rehashedPassword;
        }

        account.LastLoginAt = now;
        account.PasswordFailureCount = 0;
        account.PasswordFailureWindowStartedAt = null;
        await memberAccountRepository.RecordPasswordSignInAsync(
            account.Id,
            now,
            rehashedPassword,
            cancellationToken);
        return MemberAccountResult.Success(account);
    }

    public async Task<ExternalLoginResolution> FindOrCreateFromExternalLoginAsync(
        string provider,
        string providerKey,
        string? email,
        string? displayName,
        bool emailVerified,
        CancellationToken cancellationToken = default)
    {
        provider = MemberAuthenticationSchemes.NormalizeExternalProvider(provider) ?? provider;

        var existingByLogin = await memberAccountRepository.FindByExternalLoginAsync(provider, providerKey, cancellationToken);
        if (existingByLogin is not null)
        {
            if (existingByLogin.IsSuspended)
            {
                return ExternalLoginResolution.Suspended(existingByLogin);
            }

            var signedIn = await FinishExistingSignInAsync(existingByLogin, cancellationToken);
            return ExternalLoginResolution.SignedIn(signedIn);
        }

        // Email is only a matching key after the provider has verified it.
        // An unverified address must not create an account that can later collide.
        if (!emailVerified || string.IsNullOrWhiteSpace(email))
        {
            return ExternalLoginResolution.Unverified();
        }

        email = email.Trim();
        var existingByEmail = await memberAccountRepository.FindByEmailAsync(email, cancellationToken);
        if (existingByEmail is not null)
        {
            if (existingByEmail.IsSuspended)
            {
                return ExternalLoginResolution.Suspended(existingByEmail);
            }

            return ExternalLoginResolution.ConfirmationRequired(existingByEmail);
        }

        var account = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName.Trim(),
            CreatedAt = DateTime.UtcNow,
        };

        var created = await memberAccountRepository.CreateAsync(account, cancellationToken);
        await memberAccountRepository.AddExternalLoginAsync(created.Id, provider, providerKey, email, cancellationToken);
        await memberAccountRepository.RecordLoginAsync(created.Id, DateTime.UtcNow, cancellationToken);
        created = await memberAccountRepository.FindByIdAsync(created.Id, cancellationToken) ?? created;
        return ExternalLoginResolution.SignedIn(created);
    }

    /// <summary>
    /// Attaches a pending provider subject to the account the member just authenticated.
    /// The caller must already have proved that session with the account password or an
    /// already-linked provider. The provider email must be that account's email.
    /// </summary>
    public async Task<MemberAccountResult> LinkExternalLoginAsync(
        Guid authenticatedMemberId,
        string provider,
        string providerKey,
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedProvider = MemberAuthenticationSchemes.NormalizeExternalProvider(provider);
        if (normalizedProvider is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Invalid, ExternalLoginMessages.UnknownProvider);
        }

        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email))
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Invalid, ExternalLoginMessages.UnverifiedEmail);
        }

        var account = await memberAccountRepository.FindByIdAsync(authenticatedMemberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        if (account.IsSuspended)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Suspended, SuspendedSignInError);
        }

        if (!ExternalLoginEmail.EmailsMatch(account.Email, email))
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Invalid, ExternalLoginMessages.EmailMismatch);
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.PendingDeletion, MemberProfileService.PendingDeletionEditError);
        }

        var existingLogin = await memberAccountRepository.FindByExternalLoginAsync(
            normalizedProvider,
            providerKey,
            cancellationToken);
        if (existingLogin is not null && existingLogin.Id != account.Id)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.Conflict, ExternalLoginMessages.AlreadyLinked);
        }

        if (existingLogin is null)
        {
            await memberAccountRepository.AddExternalLoginAsync(
                account.Id,
                normalizedProvider,
                providerKey,
                email.Trim(),
                cancellationToken);
        }

        account = await FinishExistingSignInAsync(account, cancellationToken);
        return MemberAccountResult.Success(account);
    }

    /// <summary>
    /// Existing accounts only: silent legacy backfill for people who never claimed yet,
    /// then record the login. New accounts skip this so Settings can offer an explicit claim.
    /// </summary>
    private async Task<MemberAccount> FinishExistingSignInAsync(
        MemberAccount account,
        CancellationToken cancellationToken)
    {
        if (account.DeletionRequestedAt is null)
        {
            account = await legacyLinks.TryBackfillLegacyLinkAsync(account, cancellationToken);
        }

        await memberAccountRepository.RecordLoginAsync(account.Id, DateTime.UtcNow, cancellationToken);
        return await memberAccountRepository.FindByIdAsync(account.Id, cancellationToken) ?? account;
    }

    public async Task<MemberAccount?> FindByIdAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);

    public Task<MemberAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        memberAccountRepository.FindByEmailAsync(email, cancellationToken);

    public async Task<IReadOnlyList<string>> ListExternalProvidersAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        await memberAccountRepository.ListExternalProvidersAsync(memberId, cancellationToken);

    public const string SuspendedSignInError = "This account has been suspended.";

    public const string InvalidPasswordSignInError = "Incorrect email or password.";
}
