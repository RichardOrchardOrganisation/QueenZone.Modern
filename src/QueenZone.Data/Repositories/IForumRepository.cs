namespace QueenZone.Data;

public interface IForumRepository
{
    Task<IReadOnlyList<ForumCategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<ForumCategoryItem?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<ForumCategoryTopicsPage> GetCategoryTopicsPageAsync(
        int forumId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ForumTopicPostsPage?> GetTopicPostsPageAsync(
        int topicId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<int> GetTotalThreadCountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ForumRecentThreadItem>> GetRecentThreadsAsync(
        int count,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ForumRecentThreadItem>> GetLegacyDiscographyThreadsAsync(
        CancellationToken cancellationToken = default);

    Task<ForumArchiveStats> GetArchiveStatsAsync(CancellationToken cancellationToken = default);

    Task<int> GetTopicSitemapCountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ForumTopicSitemapItem>> GetTopicSitemapPageAsync(
        int offset,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ForumSearchPage> SearchForumAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a legacy topic or reply ID to its visible topic and position, or null when the
    /// post is missing or hidden.
    /// </summary>
    Task<ForumLegacyPostLocation?> FindLegacyPostAsync(
        int legacyPostId,
        CancellationToken cancellationToken = default);
}
