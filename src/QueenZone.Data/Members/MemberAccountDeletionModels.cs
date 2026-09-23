using QueenZone.Data.Entities;

namespace QueenZone.Data;

public static class MemberAccountDeletionPolicy
{
    public const string DeletedDisplayName = "Deleted member";

    public const int RetentionDays = 30;

    public const string RequestedAuditAction = "Requested";

    public const string ImmediateRequestedAuditAction = "ImmediateRequested";

    public const string CancelledAuditAction = "Cancelled";

    public const string PurgedAuditAction = "PersonalDataPurged";

    public static string CreateDeletedEmail(Guid memberId) =>
        $"deleted-{memberId:N}@deleted.invalid";

    public static string ToAvatarThumbnailPath(string avatarPath) =>
        avatarPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            ? avatarPath[..^5] + "-thumb.webp"
            : avatarPath + "-thumb";
}

public sealed record MemberAccountDeletionRequestResult(
    MemberAccount Account,
    bool AlreadyRequested);

public sealed record MemberAccountDeletionPurgeResult(
    int PurgedCount,
    IReadOnlyList<string> AvatarBlobPaths,
    IReadOnlyList<MemberDeletionBlob>? ContentBlobs = null);

public sealed record MemberDeletionBlob(Guid MemberAccountId, string Container, string Path);

public sealed record PendingMemberDeletionBlob(Guid Id, Guid MemberAccountId, string Container, string Path);

public sealed record PendingAppleRevocation(Guid ExternalLoginId, string ProtectedToken);

public sealed record DueMemberPromotions(
    IReadOnlyList<int> PhotoIds,
    IReadOnlyList<int> FanPerformanceIds);

public sealed record MemberDeletionProgress(bool IsComplete);
