namespace QueenZone.Data.Entities;

public sealed class MemberAccount
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string NormalizedEmail { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Blob path within the ugc-avatars container (not a full URL), e.g.
    /// <c>members/{guid}/avatar-{id}.webp</c>. Null when the member has no custom avatar.
    /// </summary>
    public string? AvatarUrl { get; set; }

    public string? PasswordHash { get; set; }

    /// <summary>
    /// Failed password attempts in the current lockout window. Shared by the website
    /// form and the mobile password grant.
    /// </summary>
    public int PasswordFailureCount { get; set; }

    /// <summary>
    /// Start of the current password-failure window. Null when the counter is clear.
    /// </summary>
    public DateTime? PasswordFailureWindowStartedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public int? LinkedLegacyUserId { get; set; }

    /// <summary>
    /// Who may start a new private conversation with this member.
    /// Replies in existing conversations are not gated by this setting.
    /// </summary>
    public MemberMessagePrivacy MessagePrivacy { get; set; }

    public bool IsSuspended { get; set; }

    public DateTime? SuspendedAt { get; set; }

    public string? SuspendedReason { get; set; }

    public string? SuspendedByAdminEmail { get; set; }

    public DateTime? DeletionRequestedAt { get; set; }

    /// <summary>
    /// Private recovery snapshot retained only during the deletion cooling-off period.
    /// </summary>
    public string? DeletionRecoveryDisplayName { get; set; }

    /// <summary>
    /// Private avatar blob-path snapshot retained only during the deletion cooling-off period.
    /// </summary>
    public string? DeletionRecoveryAvatarUrl { get; set; }

    public DateTime? PersonalDataPurgedAt { get; set; }
}
