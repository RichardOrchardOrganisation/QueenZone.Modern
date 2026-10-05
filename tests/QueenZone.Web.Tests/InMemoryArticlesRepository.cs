using QueenZone.Data;

namespace QueenZone.Web.Tests;

internal sealed class InMemoryArticlesRepository : IArticlesRepository
{
    private readonly IReadOnlyList<ArticleItem> publishedItems;

    public InMemoryArticlesRepository(IEnumerable<ArticleItem> rawItems)
    {
        publishedItems = ArticleItemOrdering.ByCreatedDateDescending(
            rawItems.Where(item => item.IsPublished));
    }

    public Task<IReadOnlyList<ArticleItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ArticleItem>>(publishedItems.Take(count).ToList());

    public Task<IReadOnlyList<ArticleItem>> GetArchivePageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var skip = Math.Max(page - 1, 0) * pageSize;
        return Task.FromResult<IReadOnlyList<ArticleItem>>(publishedItems.Skip(skip).Take(pageSize).ToList());
    }

    public Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(publishedItems.Count);

    public Task<IReadOnlyList<ArticleFeedKey>> GetPublishedFeedKeysAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ArticleFeedKey>>(
            publishedItems.Select(item => ArticleFeedKey.Archive(item.Id, item.PublishedAt)).ToList());

    public Task<IReadOnlyList<ArticleItem>> GetPublishedByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        var set = ids.ToHashSet();
        return Task.FromResult<IReadOnlyList<ArticleItem>>(
            publishedItems.Where(item => set.Contains(item.Id)).ToList());
    }

    public Task<ArticleItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(publishedItems.SingleOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SitemapContentEntry>>(publishedItems
            .Select(item => new SitemapContentEntry(item.Id, item.Title, item.PublishedAt))
            .ToList());
}
