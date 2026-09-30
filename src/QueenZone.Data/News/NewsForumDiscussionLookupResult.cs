namespace QueenZone.Data;

/// <summary>
/// Result of a news-detail discussion lookup. A null <see cref="ReplyCount"/> means no visible
/// forum thread exists for the topic (omit the discussion block). A zero reply count is a
/// healthy thread that has only an opening post.
/// </summary>
public readonly record struct NewsForumDiscussionLookupResult(
    int? ReplyCount,
    IReadOnlyList<NewsDiscussionPreview> Preview)
{
    public static NewsForumDiscussionLookupResult Missing { get; } = new(null, []);

    public static NewsForumDiscussionLookupResult Found(
        int replyCount,
        IReadOnlyList<NewsDiscussionPreview> preview) =>
        new(replyCount, preview);

    public bool ThreadFound => ReplyCount is not null;
}
