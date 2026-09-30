namespace QueenZone.Data;

/// <summary>
/// Batched discussion fields for news list/detail. Does not load list bodies.
/// </summary>
public interface INewsForumDiscussionLookup
{
    Task<IReadOnlyDictionary<int, int>> GetReplyCountsAsync(
        IReadOnlyList<int> topicIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads reply count and last-N reply preview for a news discussion topic.
    /// <see cref="NewsForumDiscussionLookupResult.ReplyCount"/> is <see langword="null"/> when
    /// no visible <c>ModernForumThreads</c> row exists for <paramref name="topicId"/>.
    /// </summary>
    Task<NewsForumDiscussionLookupResult> GetDiscussionAsync(
        int topicId,
        int previewCount,
        CancellationToken cancellationToken = default);
}
