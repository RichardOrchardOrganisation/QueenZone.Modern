using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Storage;

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
public sealed class MemberAccountService(
    IMemberAccountRepository memberAccountRepository,
    LegacyAccountLinkService legacyLinks,
    IBlobUploadService blobUploadService,
    TimeProvider? timeProvider = null,
    IOptions<PasswordSignInLockoutOptions>? passwordLockout = null,
    MemberAccountDeletionService? deletionService = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    private readonly MemberAccountDeletionService deletions = deletionService
        ?? new MemberAccountDeletionService(memberAccountRepository, blobUploadService, timeProvider);

    private readonly PasswordSignInLockoutOptions lockout = passwordLockout?.Value ?? new PasswordSignInLockoutOptions();
    /// <summary>
    /// Bound for each avatar blob delete. <see cref="MemberAccountDeletionHostedService"/>
    /// can invoke this on its first timer tick; Azure.Storage.Blobs often ignores
    /// <see cref="CancellationToken"/> during a dead TCP or retry loop, so callers
    /// use both <c>CancelAfter</c> and <c>WaitAsync</c>.
    /// </summary>
    internal static readonly TimeSpan DefaultBlobDeleteTimeout = TimeSpan.FromSeconds(20);

    internal TimeSpan BlobDeleteTimeout { get; init; } = DefaultBlobDeleteTimeout;

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

    public async Task<MemberAccount?> SuspendAsync(
        Guid memberId,
        string reason,
        string suspendedByAdminEmail,
        DateTime suspendedAt,
        CancellationToken cancellationToken = default) =>
        await memberAccountRepository.SuspendAsync(memberId, reason, suspendedByAdminEmail, suspendedAt, cancellationToken);

    public async Task<IReadOnlyList<string>> ListExternalProvidersAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        await memberAccountRepository.ListExternalProvidersAsync(memberId, cancellationToken);

    public async Task<MemberAccountResult> RequestDeletionAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var requested = await memberAccountRepository.RequestDeletionAsync(
            memberId,
            DateTime.UtcNow,
            cancellationToken);
        if (requested is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        return MemberAccountResult.Success(requested.Account);
    }

    public Task<MemberAccountResult> DeleteImmediatelyAsync(
        Guid memberId, CancellationToken cancellationToken = default) =>
        deletions.DeleteImmediatelyAsync(memberId, BlobDeleteTimeout, cancellationToken);

    public async Task<MemberAccountResult> CancelDeletionAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.CancelDeletionAsync(
            memberId,
            DateTime.UtcNow,
            cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.NotFound, "Account not found.");
        }

        if (account.PersonalDataPurgedAt is not null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.DeletionNotCancellable, "Account deletion can no longer be cancelled.");
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(MemberAccountOutcome.DeletionNotCancellable, "The 30-day cooling-off period has ended.");
        }

        return MemberAccountResult.Success(account);
    }

    public Task<int> PurgeDueDeletionsAsync(
        DateTime utcNow, CancellationToken cancellationToken = default) =>
        deletions.PurgeDueDeletionsAsync(utcNow, BlobDeleteTimeout, cancellationToken);

    public const string ImmediatePurgeIncompleteError =
        "Account deletion could not finish removing personal data. Try again shortly.";

    public const string SuspendedSignInError = "This account has been suspended.";

    public const string InvalidPasswordSignInError = "Incorrect email or password.";
}

/// <summary>Typed outcome of a member account operation so callers never branch on message text (#2436).</summary>
public enum MemberAccountOutcome
{
    Succeeded,
    NotFound,
    Invalid,
    InvalidCredentials,
    Suspended,
    PendingDeletion,
    Conflict,
    QuotaExceeded,
    UploadFailed,
    DeletionNotCancellable,
    PurgeIncomplete,
}

public sealed record MemberAccountResult(MemberAccountOutcome Outcome, MemberAccount? Account, string? Error)
{
    public bool Succeeded => Outcome == MemberAccountOutcome.Succeeded;

    public static MemberAccountResult Success(MemberAccount account) => new(MemberAccountOutcome.Succeeded, account, null);

    public static MemberAccountResult Failure(MemberAccountOutcome outcome, string error) => new(outcome, null, error);
}

public sealed record MemberSocialLinkFieldError(MemberSocialChannel Channel, string Message);

public sealed record MemberSocialLinksUpdateResult(
    MemberAccountOutcome Outcome,
    string? Error,
    IReadOnlyList<MemberSocialLink> Links,
    IReadOnlyList<MemberSocialLinkFieldError> FieldErrors)
{
    public bool Succeeded => Outcome == MemberAccountOutcome.Succeeded;

    public static MemberSocialLinksUpdateResult Success(IReadOnlyList<MemberSocialLink> links) =>
        new(MemberAccountOutcome.Succeeded, null, links, []);

    public static MemberSocialLinksUpdateResult Failure(MemberAccountOutcome outcome, string error) =>
        new(outcome, error, [], []);

    public static MemberSocialLinksUpdateResult Invalid(
        IReadOnlyList<MemberSocialLink> existing,
        IReadOnlyList<MemberSocialLinkFieldError> fieldErrors) =>
        new(MemberAccountOutcome.Invalid, null, existing, fieldErrors);
}

public enum LegacyAccountLinkKind
{
    None = 0,
    Linked = 1,
    Claimable = 2,
    Unavailable = 3,
}

public sealed record LegacyAccountLinkState(
    LegacyAccountLinkKind Kind,
    LegacyMemberMatch? Match,
    IReadOnlyList<LegacyMemberMatch> ClaimableMatches,
    IReadOnlyList<LegacyMemberMatch> UnavailableMatches)
{
    public bool HasMultipleClaimable => ClaimableMatches.Count > 1;

    public static LegacyAccountLinkState None() =>
        new(LegacyAccountLinkKind.None, null, [], []);

    public static LegacyAccountLinkState Linked(LegacyMemberMatch match) =>
        new(LegacyAccountLinkKind.Linked, match, [], []);

    public static LegacyAccountLinkState Claimable(
        IReadOnlyList<LegacyMemberMatch> claimableMatches,
        IReadOnlyList<LegacyMemberMatch>? unavailableMatches = null)
    {
        ArgumentNullException.ThrowIfNull(claimableMatches);
        if (claimableMatches.Count == 0)
        {
            throw new ArgumentException("At least one claimable match is required.", nameof(claimableMatches));
        }

        var unavailable = unavailableMatches ?? [];
        // Match is set for the common single-match UI/tests; multi-match leaves it null.
        var primary = claimableMatches.Count == 1 ? claimableMatches[0] : null;
        return new(LegacyAccountLinkKind.Claimable, primary, claimableMatches, unavailable);
    }

    public static LegacyAccountLinkState Claimable(LegacyMemberMatch match) =>
        Claimable([match], []);

    public static LegacyAccountLinkState Unavailable(IReadOnlyList<LegacyMemberMatch> unavailableMatches)
    {
        ArgumentNullException.ThrowIfNull(unavailableMatches);
        var primary = unavailableMatches.Count > 0 ? unavailableMatches[0] : null;
        return new(LegacyAccountLinkKind.Unavailable, primary, [], unavailableMatches);
    }

    public static LegacyAccountLinkState Unavailable(LegacyMemberMatch match) =>
        Unavailable([match]);
}
