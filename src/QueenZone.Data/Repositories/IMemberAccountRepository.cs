using QueenZone.Data.Entities;

namespace QueenZone.Data;

public interface IMemberAccountRepository
{
    Task<MemberAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<MemberAccount?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Display names for the requested member ids, loaded in one query.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ListDisplayNamesAsync(
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The subset of <paramref name="memberIds"/> that exist and have not requested deletion.
    /// Batch form of <see cref="FindByIdAsync"/> for callers filtering a list of member ids.
    /// </summary>
    Task<IReadOnlySet<Guid>> ListActiveMemberIdsAsync(
        IReadOnlyCollection<Guid> memberIds,
        CancellationToken cancellationToken = default);

    Task<MemberAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListExternalProvidersAsync(Guid memberAccountId, CancellationToken cancellationToken = default);

    Task<MemberAccount> CreateAsync(MemberAccount account, CancellationToken cancellationToken = default);

    Task AddExternalLoginAsync(Guid memberAccountId, string provider, string providerKey, string email, CancellationToken cancellationToken = default);

    Task SaveAppleRefreshTokenAsync(
        Guid memberAccountId,
        string providerKey,
        string protectedToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingAppleRevocation>> ListPendingAppleRevocationsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task CompleteAppleRevocationAsync(Guid externalLoginId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates <see cref="MemberAccount.DisplayName"/> for the given member.
    /// Display names are not unique — multiple members may share the same name.
    /// </summary>
    Task<MemberAccount?> UpdateDisplayNameAsync(Guid memberId, string displayName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or clears <see cref="MemberAccount.AvatarUrl"/> (blob path within ugc-avatars).
    /// Pass null to remove the avatar.
    /// </summary>
    Task<MemberAccount?> UpdateAvatarUrlAsync(Guid memberId, string? avatarBlobPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates who may start a new private conversation with this member.
    /// </summary>
    Task<MemberAccount?> UpdateMessagePrivacyAsync(
        Guid memberId,
        MemberMessagePrivacy messagePrivacy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the modern account already linked to the given legacy USERS_T id, if any.
    /// </summary>
    Task<MemberAccount?> FindByLinkedLegacyUserIdAsync(int legacyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets <see cref="MemberAccount.LinkedLegacyUserId"/> when the member is still unlinked.
    /// Callers must enforce uniqueness before calling.
    /// </summary>
    Task<MemberAccount?> LinkLegacyUserIdAsync(Guid memberId, int legacyUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears <see cref="MemberAccount.LinkedLegacyUserId"/> so the member can claim a different
    /// free legacy account (or remain unlinked).
    /// </summary>
    Task<MemberAccount?> UnlinkLegacyUserIdAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task RecordLoginAsync(Guid memberId, DateTime loginAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the password-failure counter for the current lockout window.
    /// </summary>
    Task RecordPasswordFailureAsync(
        Guid memberId,
        int failureCount,
        DateTime windowStartedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a successful password sign-in: last login, a cleared failure counter,
    /// and an optional rewritten password hash.
    /// </summary>
    Task RecordPasswordSignInAsync(
        Guid memberId,
        DateTime loginAt,
        string? rehashedPassword,
        CancellationToken cancellationToken = default);

    Task<MemberStats> GetStatsAsync(DateTime utcNow, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentLogin>> GetRecentLoginsAsync(int count, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DailyRegistration>> GetDailyRegistrationsAsync(DateOnly fromDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds members whose display name contains <paramref name="query"/> (case-insensitive).
    /// Display names are not unique — callers must present matches for selection.
    /// </summary>
    Task<IReadOnlyList<MemberRecipientMatch>> SearchByDisplayNameAsync(
        string query,
        Guid? excludeMemberId = null,
        int maxResults = PrivateMessageLimits.MaxRecipientSearchResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Admin member lookup: matches <paramref name="query"/> against display name or email
    /// (case-insensitive, substring), newest members first. Pass null/empty to list everyone.
    /// </summary>
    Task<MemberSearchResult> SearchMembersAsync(
        string? query,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists accounts that currently have local email/password sign-in enabled.
    /// Password hashes are deliberately excluded from the returned projection.
    /// </summary>
    Task<IReadOnlyList<LocalPasswordAccountSummary>> ListLocalPasswordAccountsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the editable details for an existing local-password account.
    /// A null password hash leaves the current password unchanged.
    /// </summary>
    Task<MemberAccount?> UpdateLocalPasswordAccountAsync(
        Guid memberId,
        string email,
        string displayName,
        string? passwordHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes local password sign-in without deleting the member or their content.
    /// </summary>
    Task<bool> RemoveLocalPasswordAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a member account suspended, blocking new sign-ins and (via cookie re-validation)
    /// ending any existing session. Posts and other content are left untouched.
    /// </summary>
    Task<MemberAccount?> SuspendAsync(
        Guid memberId,
        string reason,
        string suspendedByAdminEmail,
        DateTime suspendedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears a suspension, restoring normal sign-in access.
    /// </summary>
    Task<MemberAccount?> ReinstateAsync(Guid memberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules deletion after the policy cooling-off period. The account remains usable and
    /// unchanged until it is cancelled or <see cref="PurgeDeletedAccountsAsync"/> runs.
    /// </summary>
    Task<MemberAccountDeletionRequestResult?> RequestDeletionAsync(
        Guid memberId,
        DateTime requestedAt,
        CancellationToken cancellationToken = default,
        bool immediate = false);

    /// <summary>
    /// Cancels a pending deletion request before personal data has been purged.
    /// </summary>
    Task<MemberAccount?> CancelDeletionAsync(
        Guid memberId,
        DateTime cancelledAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Irreversibly removes personal and authentication data for deletion requests at or before
    /// <paramref name="purgeBefore"/>. A non-personal member tombstone remains.
    /// </summary>
    Task<MemberAccountDeletionPurgeResult> PurgeDeletedAccountsAsync(
        DateTime purgeBefore,
        DateTime purgedAt,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PendingMemberDeletionBlob>> ListPendingDeletionBlobsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task CompleteDeletionBlobAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MemberDeletionProgress?> GetDeletionProgressAsync(Guid memberId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemberSocialLink>> ListSocialLinksAsync(
        Guid memberId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the member's social-link rows with <paramref name="links"/>.
    /// Pass an empty list to remove every channel. Callers must not send
    /// duplicate channels; the unique (<c>MemberId</c>, <c>Channel</c>) key rejects them.
    /// </summary>
    Task ReplaceSocialLinksAsync(
        Guid memberId,
        IReadOnlyList<MemberSocialLink> links,
        CancellationToken cancellationToken = default);
}

public sealed record LocalPasswordAccountSummary(
    Guid Id,
    string Email,
    string DisplayName,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool IsSuspended);
