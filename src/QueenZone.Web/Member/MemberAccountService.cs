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
    ILegacyMemberLookupRepository legacyMemberLookupRepository,
    IBlobUploadService blobUploadService,
    MemberUploadQuotaService uploadQuota,
    TimeProvider? timeProvider = null,
    IOptions<PasswordSignInLockoutOptions>? passwordLockout = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

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
            return MemberAccountResult.Failure("An account with that email already exists.");
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
            return MemberAccountResult.Failure(InvalidPasswordSignInError);
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

            return MemberAccountResult.Failure(InvalidPasswordSignInError);
        }

        var rehashedPassword = verification == PasswordVerificationResult.SuccessRehashNeeded
            ? passwordHasher.HashPassword(account, password)
            : null;
        account = await TryBackfillLegacyLinkAsync(account, cancellationToken);
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
            return MemberAccountResult.Failure(ExternalLoginMessages.UnknownProvider);
        }

        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email))
        {
            return MemberAccountResult.Failure(ExternalLoginMessages.UnverifiedEmail);
        }

        var account = await memberAccountRepository.FindByIdAsync(authenticatedMemberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        if (account.IsSuspended)
        {
            return MemberAccountResult.Failure(SuspendedSignInError);
        }

        if (!ExternalLoginEmail.EmailsMatch(account.Email, email))
        {
            return MemberAccountResult.Failure(ExternalLoginMessages.EmailMismatch);
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(PendingDeletionEditError);
        }

        var existingLogin = await memberAccountRepository.FindByExternalLoginAsync(
            normalizedProvider,
            providerKey,
            cancellationToken);
        if (existingLogin is not null && existingLogin.Id != account.Id)
        {
            return MemberAccountResult.Failure(ExternalLoginMessages.AlreadyLinked);
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
            account = await TryBackfillLegacyLinkAsync(account, cancellationToken);
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

    /// <summary>
    /// Resolves whether the member is already linked to a legacy account, or can claim one
    /// or more free USERS_T rows that match their modern email.
    /// </summary>
    public async Task<LegacyAccountLinkState> GetLegacyLinkStateAsync(
        MemberAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.LinkedLegacyUserId is int linkedId)
        {
            var linked = await legacyMemberLookupRepository.FindByUserIdAsync(linkedId, cancellationToken)
                ?? new LegacyMemberMatch(linkedId, $"User #{linkedId}");
            return LegacyAccountLinkState.Linked(linked);
        }

        var matches = await legacyMemberLookupRepository.FindAllByEmailAsync(account.Email, cancellationToken);
        if (matches.Count == 0)
        {
            return LegacyAccountLinkState.None();
        }

        var claimable = new List<LegacyMemberMatch>(matches.Count);
        var unavailable = new List<LegacyMemberMatch>();
        foreach (var match in matches)
        {
            var takenBy = await memberAccountRepository.FindByLinkedLegacyUserIdAsync(match.UserId, cancellationToken);
            if (takenBy is not null && takenBy.Id != account.Id)
            {
                unavailable.Add(match);
            }
            else
            {
                claimable.Add(match);
            }
        }

        if (claimable.Count > 0)
        {
            return LegacyAccountLinkState.Claimable(claimable, unavailable);
        }

        return LegacyAccountLinkState.Unavailable(unavailable);
    }

    /// <summary>
    /// Explicitly claims a free legacy USERS_T account that matches the member's email.
    /// When multiple rows share that email, <paramref name="legacyUserId"/> selects which one.
    /// Optionally adopts the legacy username as the modern display name.
    /// </summary>
    public async Task<MemberAccountResult> ClaimLegacyAccountAsync(
        Guid memberId,
        int legacyUserId,
        bool adoptLegacyDisplayName,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        if (account.LinkedLegacyUserId is not null)
        {
            return MemberAccountResult.Success(account);
        }

        var matches = await legacyMemberLookupRepository.FindAllByEmailAsync(account.Email, cancellationToken);
        var match = matches.FirstOrDefault(item => item.UserId == legacyUserId);
        if (match is null)
        {
            return MemberAccountResult.Failure(
                "That legacy account does not match your email, or was not found.");
        }

        var takenBy = await memberAccountRepository.FindByLinkedLegacyUserIdAsync(match.UserId, cancellationToken);
        if (takenBy is not null && takenBy.Id != memberId)
        {
            return MemberAccountResult.Failure(
                "That legacy account is already linked to another modern member.");
        }

        var linked = await memberAccountRepository.LinkLegacyUserIdAsync(memberId, match.UserId, cancellationToken);
        if (linked is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        if (adoptLegacyDisplayName && !string.IsNullOrWhiteSpace(match.Username))
        {
            var renamed = await UpdateDisplayNameAsync(memberId, match.Username, cancellationToken);
            if (renamed.Succeeded && renamed.Account is not null)
            {
                return renamed;
            }
        }

        return MemberAccountResult.Success(linked);
    }

    /// <summary>
    /// Clears the member's legacy USERS_T link so they can claim a different free match
    /// (or remain unlinked). Does not change display name.
    /// </summary>
    public async Task<MemberAccountResult> UnlinkLegacyAccountAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        if (account.LinkedLegacyUserId is null)
        {
            return MemberAccountResult.Success(account);
        }

        var unlinked = await memberAccountRepository.UnlinkLegacyUserIdAsync(memberId, cancellationToken);
        if (unlinked is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        return MemberAccountResult.Success(unlinked);
    }

    /// <summary>
    /// Silently links a free legacy account when the member is still unlinked and their email
    /// matches exactly one free USERS_T row. Used to backfill accounts created before the claim UI
    /// existed. Multiple free matches are left unlinked so the member can choose in Settings.
    /// </summary>
    private async Task<MemberAccount> TryBackfillLegacyLinkAsync(
        MemberAccount account,
        CancellationToken cancellationToken)
    {
        if (account.LinkedLegacyUserId is not null)
        {
            return account;
        }

        var matches = await legacyMemberLookupRepository.FindAllByEmailAsync(account.Email, cancellationToken);
        if (matches.Count == 0)
        {
            return account;
        }

        LegacyMemberMatch? soleFree = null;
        foreach (var match in matches)
        {
            var takenBy = await memberAccountRepository.FindByLinkedLegacyUserIdAsync(match.UserId, cancellationToken);
            if (takenBy is not null && takenBy.Id != account.Id)
            {
                continue;
            }

            if (soleFree is not null)
            {
                // Ambiguous: more than one free legacy account for this email.
                return account;
            }

            soleFree = match;
        }

        if (soleFree is null)
        {
            return account;
        }

        return await memberAccountRepository.LinkLegacyUserIdAsync(account.Id, soleFree.UserId, cancellationToken)
            ?? account;
    }

    /// <summary>
    /// Updates the member's display name. Names are not unique — multiple members may share
    /// the same display name (common on forums). Leading/trailing whitespace is stripped.
    /// </summary>
    public async Task<MemberAccountResult> UpdateDisplayNameAsync(Guid memberId, string displayName, CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account?.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(PendingDeletionEditError);
        }

        var trimmed = displayName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return MemberAccountResult.Failure("Display name is required.");
        }

        if (trimmed.Length < MinDisplayNameLength)
        {
            return MemberAccountResult.Failure($"Display name must be at least {MinDisplayNameLength} characters.");
        }

        if (trimmed.Length > MaxDisplayNameLength)
        {
            return MemberAccountResult.Failure($"Display name must be at most {MaxDisplayNameLength} characters.");
        }

        var updated = await memberAccountRepository.UpdateDisplayNameAsync(memberId, trimmed, cancellationToken);
        if (updated is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        return MemberAccountResult.Success(updated);
    }

    /// <summary>
    /// Updates who may start a new private conversation with this member.
    /// </summary>
    public async Task<MemberAccountResult> UpdateMessagePrivacyAsync(
        Guid memberId,
        MemberMessagePrivacy messagePrivacy,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(PendingDeletionEditError);
        }

        if (!Enum.IsDefined(messagePrivacy))
        {
            return MemberAccountResult.Failure("Choose a valid messaging privacy option.");
        }

        var updated = await memberAccountRepository.UpdateMessagePrivacyAsync(
            memberId,
            messagePrivacy,
            cancellationToken);
        if (updated is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        return MemberAccountResult.Success(updated);
    }

    public Task<IReadOnlyList<MemberSocialLink>> ListSocialLinksAsync(
        Guid memberId,
        CancellationToken cancellationToken = default) =>
        memberAccountRepository.ListSocialLinksAsync(memberId, cancellationToken);

    /// <summary>
    /// Validates each channel independently. Empty input removes that row. Invalid input
    /// is a field error and leaves every existing row untouched.
    /// </summary>
    public async Task<MemberSocialLinksUpdateResult> UpdateSocialLinksAsync(
        Guid memberId,
        IReadOnlyDictionary<MemberSocialChannel, string?> inputs,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberSocialLinksUpdateResult.Failure("Account not found.");
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberSocialLinksUpdateResult.Failure(PendingDeletionEditError);
        }

        var fieldErrors = new List<MemberSocialLinkFieldError>();
        var accepted = new List<MemberSocialLink>();
        foreach (var channel in MemberSocialChannels.All)
        {
            inputs.TryGetValue(channel, out var raw);
            if (!MemberSocialLinkUrl.TryNormalize(channel, raw, out var canonicalUrl, out var error))
            {
                fieldErrors.Add(new MemberSocialLinkFieldError(channel, error));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(canonicalUrl))
            {
                accepted.Add(new MemberSocialLink(channel, canonicalUrl));
            }
        }

        if (fieldErrors.Count > 0)
        {
            var existing = await memberAccountRepository.ListSocialLinksAsync(memberId, cancellationToken);
            return MemberSocialLinksUpdateResult.Invalid(existing, fieldErrors);
        }

        await memberAccountRepository.ReplaceSocialLinksAsync(memberId, accepted, cancellationToken);
        return MemberSocialLinksUpdateResult.Success(accepted);
    }

    /// <summary>
    /// Validates and processes an avatar upload, stores full + thumbnail WebP blobs, and
    /// updates <see cref="MemberAccount.AvatarUrl"/>. On DB failure the new blobs are deleted
    /// and the previous avatar is left in place.
    /// </summary>
    public async Task<MemberAccountResult> UpdateAvatarAsync(
        Guid memberId,
        Stream image,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(PendingDeletionEditError);
        }

        MemberAvatarImageProcessor.ProcessedAvatar processed;
        try
        {
            processed = await MemberAvatarImageProcessor.ProcessAsync(image, originalFileName, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return MemberAccountResult.Failure(ex.Message);
        }

        var principalKey = MemberUploadQuotaService.PrincipalKeyFromMemberId(memberId);
        var quotaBytes = Math.Max(processed.FullImage.Length, 1);
        if (!uploadQuota.TryConsume(principalKey, quotaBytes, out var quotaError))
        {
            return MemberAccountResult.Failure(quotaError ?? "Daily upload limit reached.");
        }

        await using (processed.FullImage)
        await using (processed.Thumbnail)
        {
            var avatarBlobName = MemberAvatarPaths.CreateAvatarBlobName(memberId);
            var thumbBlobName = MemberAvatarPaths.ToThumbBlobName(avatarBlobName);
            var uploadContext = new BlobUploadContext
            {
                MemberAccountId = memberId,
                PreferredBlobName = avatarBlobName,
            };
            var thumbContext = new BlobUploadContext
            {
                MemberAccountId = memberId,
                PreferredBlobName = thumbBlobName,
            };

            string? uploadedAvatar = null;
            string? uploadedThumb = null;
            try
            {
                processed.FullImage.Position = 0;
                var fullResult = await blobUploadService.UploadAsync(
                    processed.FullImage,
                    "avatar.webp",
                    MemberAvatarPaths.Container,
                    uploadContext,
                    cancellationToken);
                uploadedAvatar = fullResult.BlobName;

                processed.Thumbnail.Position = 0;
                var thumbResult = await blobUploadService.UploadAsync(
                    processed.Thumbnail,
                    "avatar-thumb.webp",
                    MemberAvatarPaths.Container,
                    thumbContext,
                    cancellationToken);
                uploadedThumb = thumbResult.BlobName;

                var previousAvatar = account.AvatarUrl;
                var updated = await memberAccountRepository.UpdateAvatarUrlAsync(
                    memberId,
                    uploadedAvatar,
                    cancellationToken);
                if (updated is null)
                {
                    await SafeDeleteAsync(uploadedAvatar, uploadedThumb, cancellationToken);
                    return MemberAccountResult.Failure("Account not found.");
                }

                // Delete previous blobs only after the DB write succeeds.
                if (!string.IsNullOrWhiteSpace(previousAvatar)
                    && !string.Equals(previousAvatar, uploadedAvatar, StringComparison.Ordinal))
                {
                    await SafeDeleteAsync(
                        previousAvatar,
                        MemberAvatarPaths.ToThumbBlobName(previousAvatar),
                        cancellationToken);
                }

                return MemberAccountResult.Success(updated);
            }
            catch (NotSupportedException ex)
            {
                await SafeDeleteAsync(uploadedAvatar, uploadedThumb, cancellationToken);
                return MemberAccountResult.Failure(ex.Message);
            }
            catch (BlobUploadException ex)
            {
                await SafeDeleteAsync(uploadedAvatar, uploadedThumb, cancellationToken);
                return MemberAccountResult.Failure(ex.Message);
            }
            catch (Exception)
            {
                await SafeDeleteAsync(uploadedAvatar, uploadedThumb, cancellationToken);
                throw;
            }
        }
    }

    public async Task<MemberAccountResult> RemoveAvatarAsync(
        Guid memberId,
        CancellationToken cancellationToken = default)
    {
        var account = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (account is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure(PendingDeletionEditError);
        }

        if (string.IsNullOrWhiteSpace(account.AvatarUrl))
        {
            return MemberAccountResult.Success(account);
        }

        var previousAvatar = account.AvatarUrl;
        var updated = await memberAccountRepository.UpdateAvatarUrlAsync(memberId, null, cancellationToken);
        if (updated is null)
        {
            return MemberAccountResult.Failure("Account not found.");
        }

        await SafeDeleteAsync(
            previousAvatar,
            MemberAvatarPaths.ToThumbBlobName(previousAvatar),
            cancellationToken);

        return MemberAccountResult.Success(updated);
    }

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
            return MemberAccountResult.Failure("Account not found.");
        }

        return MemberAccountResult.Success(requested.Account);
    }

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
            return MemberAccountResult.Failure("Account not found.");
        }

        if (account.PersonalDataPurgedAt is not null)
        {
            return MemberAccountResult.Failure("Account deletion can no longer be cancelled.");
        }

        if (account.DeletionRequestedAt is not null)
        {
            return MemberAccountResult.Failure("The 30-day cooling-off period has ended.");
        }

        return MemberAccountResult.Success(account);
    }

    public async Task<int> PurgeDueDeletionsAsync(
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var result = await memberAccountRepository.PurgeDeletedAccountsAsync(
            utcNow.AddDays(-MemberAccountDeletionPolicy.RetentionDays),
            utcNow,
            cancellationToken);
        foreach (var avatarBlobPath in result.AvatarBlobPaths)
        {
            await SafeDeleteAsync(
                avatarBlobPath,
                MemberAvatarPaths.ToThumbBlobName(avatarBlobPath),
                cancellationToken);
        }

        return result.PurgedCount;
    }

    private async Task SafeDeleteAsync(
        string? avatarBlobName,
        string? thumbBlobName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(avatarBlobName))
        {
            await DeleteBlobBestEffortAsync(avatarBlobName, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(thumbBlobName))
        {
            await DeleteBlobBestEffortAsync(thumbBlobName, cancellationToken);
        }
    }

    private async Task DeleteBlobBestEffortAsync(string blobName, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BlobDeleteTimeout);

        try
        {
            // CancelAfter asks the SDK to stop; WaitAsync bounds this await even when
            // the SDK ignores the token (cold first outbound call, dead TCP, retries).
            await blobUploadService
                .DeleteAsync(MemberAvatarPaths.Container, blobName, timeout.Token)
                .WaitAsync(BlobDeleteTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Timeout, SDK cancel, or storage failure: best-effort only.
        }
    }

    public const int MinDisplayNameLength = 2;

    public const int MaxDisplayNameLength = 100;

    public const string PendingDeletionEditError = "Cancel account deletion before changing your public profile.";

    public const string SuspendedSignInError = "This account has been suspended.";

    public const string InvalidPasswordSignInError = "Incorrect email or password.";
}

public sealed record MemberAccountResult(bool Succeeded, MemberAccount? Account, string? Error)
{
    public static MemberAccountResult Success(MemberAccount account) => new(true, account, null);

    public static MemberAccountResult Failure(string error) => new(false, null, error);
}

public sealed record MemberSocialLinkFieldError(MemberSocialChannel Channel, string Message);

public sealed record MemberSocialLinksUpdateResult(
    bool Succeeded,
    string? Error,
    IReadOnlyList<MemberSocialLink> Links,
    IReadOnlyList<MemberSocialLinkFieldError> FieldErrors)
{
    public static MemberSocialLinksUpdateResult Success(IReadOnlyList<MemberSocialLink> links) =>
        new(true, null, links, []);

    public static MemberSocialLinksUpdateResult Failure(string error) =>
        new(false, error, [], []);

    public static MemberSocialLinksUpdateResult Invalid(
        IReadOnlyList<MemberSocialLink> existing,
        IReadOnlyList<MemberSocialLinkFieldError> fieldErrors) =>
        new(false, null, existing, fieldErrors);
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
