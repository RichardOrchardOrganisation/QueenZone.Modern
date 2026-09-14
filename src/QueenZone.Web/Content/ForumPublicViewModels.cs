namespace QueenZone.Web;

/// <summary>
/// Stable post shape for public topic pages.
/// </summary>
public sealed record ForumPostViewModel(
    int Id,
    string Body,
    DateTime PostedAt,
    string AuthorUsername,
    string? Signature,
    DateTime? AuthorMemberSince,
    IReadOnlyList<ForumAttachmentViewModel> Attachments,
    Guid? AuthorMemberId = null,
    DateTimeOffset? EditedAt = null,
    int EditCount = 0,
    bool CanEdit = false,
    string? EditedLabel = null,
    bool CanMessage = false,
    bool IsAuthor = false,
    int? AuthorLegacyUserId = null,
    bool AlreadyReported = false,
    bool IsBlocked = false);

/// <summary>
/// Stable attachment presentation for forum posts.
/// </summary>
public sealed record ForumAttachmentViewModel(
    string FileName,
    string Url,
    string Extension,
    string FormattedSize,
    bool IsImage = false,
    string? ThumbnailUrl = null);

/// <summary>
/// Stable aggregate counts for the public forum index.
/// </summary>
public sealed record ForumIndexStats(
    int ForumCount,
    int ThreadCount,
    long PostCount);

/// <summary>
/// Cross-board recent thread row for the public forum index activity feed.
/// </summary>
public sealed record ForumRecentThreadSummary(
    int TopicId,
    string Title,
    string DetailPath,
    int CategoryId,
    string CategoryName,
    string CategoryPath,
    int ReplyCount,
    DateTime LastActivityAt);
