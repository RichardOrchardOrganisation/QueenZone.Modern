namespace QueenZone.Data;

public sealed record NewForumThread(
    int CategoryId,
    Guid AuthorMemberId,
    string AuthorDisplayName,
    string Subject,
    string Body,
    DateTimeOffset CreatedAt,
    NewForumPoll? Poll = null);

public sealed record NewForumPost(
    int TopicId,
    Guid AuthorMemberId,
    string AuthorDisplayName,
    string Body,
    DateTimeOffset CreatedAt);

public sealed record ForumWriteThread(
    int TopicId,
    int CategoryId,
    string Subject,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastPostAt,
    int PostCount,
    bool IsLocked,
    bool HasPoll = false,
    bool IsHidden = false);

public sealed record ForumThreadCreateResult(int TopicId, int StarterPostId);

public sealed record ForumEditablePost(
    int PostId,
    int TopicId,
    string TopicSubject,
    string Body,
    Guid? AuthorMemberId,
    string AuthorDisplayName,
    DateTimeOffset PostedAt,
    DateTimeOffset? EditedAt,
    int EditCount,
    int PositionInThread = 1,
    DateTimeOffset? UpdatedAt = null);

public enum ForumPostUpdateStatus
{
    Success,
    NotFound,
    Forbidden,
    EditWindowExpired,
    EditingDisabled,
    ConcurrencyConflict,
    ValidationFailed,
}

public sealed record ForumPostUpdateResult(
    ForumPostUpdateStatus Status,
    int TopicId = 0,
    string TopicSubject = "");

public sealed record ForumAuthorContentSummary(
    Guid? MemberId,
    string DisplayName,
    int PostCount,
    int ThreadCount,
    bool IsHidden);
