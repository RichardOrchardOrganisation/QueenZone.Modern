namespace QueenZone.Data;

public sealed class InMemoryArticlesRepository(IReadOnlyList<ArticleItem> seedArticles, IEditorialArticleRepository? editorialArticles = null) : IArticlesRepository
{
    private readonly IReadOnlyList<ArticleItem> publishedItems = ArticleItemOrdering.ByCreatedDateDescending(
        seedArticles.Where(item => item.IsPublished));

    public async Task<IReadOnlyList<ArticleItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        (await ApplyAsync(publishedItems.Take(count).ToList(), cancellationToken));

    public async Task<IReadOnlyList<ArticleItem>> GetArchivePageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var skip = Math.Max(page - 1, 0) * pageSize;
        return await ApplyAsync(publishedItems.Skip(skip).Take(pageSize).ToList(), cancellationToken);
    }

    public async Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default) =>
        (await ApplyAsync(publishedItems, cancellationToken)).Count;

    public async Task<ArticleItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = publishedItems.SingleOrDefault(item => item.Id == id);
        return item is null ? null : (await ApplyAsync([item], cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
        (await ApplyAsync(publishedItems, cancellationToken))
            .Select(item => new SitemapContentEntry(item.Id, item.Title, item.PublishedAt))
            .ToList();

    private async Task<IReadOnlyList<ArticleItem>> ApplyAsync(IReadOnlyList<ArticleItem> items, CancellationToken ct)
    {
        if (editorialArticles is null) return items;
        var overlays = await editorialArticles.GetPublishedLegacyOverlaysAsync(items.Select(x => x.Id), ct);
        return EditorialArticleOverlay.Apply(items, overlays);
    }
}
