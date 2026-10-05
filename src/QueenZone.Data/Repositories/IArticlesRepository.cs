namespace QueenZone.Data;

public interface IArticlesRepository
{
    Task<IReadOnlyList<ArticleItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArticleItem>> GetArchivePageAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArticleFeedKey>> GetPublishedFeedKeysAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArticleItem>> GetPublishedByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default);

    Task<ArticleItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(CancellationToken cancellationToken = default);
}
