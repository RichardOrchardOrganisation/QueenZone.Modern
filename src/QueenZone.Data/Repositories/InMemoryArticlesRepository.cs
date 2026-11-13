namespace QueenZone.Data;

public sealed class InMemoryArticlesRepository(IReadOnlyList<ArticleItem> seedArticles, IEditorialArticleRepository? editorialArticles = null) : IArticlesRepository
{
    private readonly IReadOnlyList<ArticleItem> publishedItems = ArticleItemOrdering.ByCreatedDateDescending(
        seedArticles.Where(item => item.IsPublished));

    public async Task<IReadOnlyList<ArticleItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        (await ApplyAsync(publishedItems.Take(Math.Clamp(count, 1, 100)).Select(item => item with { Body = string.Empty }).ToList(), cancellationToken));

    public async Task<IReadOnlyList<ArticleItem>> GetArchivePageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(pageSize, 1, 100);
        var skip = (Math.Max(page, 1) - 1) * take;
        return await ApplyAsync(publishedItems.Skip(skip).Take(take).Select(item => item with { Body = string.Empty }).ToList(), cancellationToken);
    }

    public async Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default) =>
        (await ApplyAsync(publishedItems, cancellationToken)).Count;

    public async Task<IReadOnlyList<ArticleFeedKey>> GetPublishedFeedKeysAsync(
        CancellationToken cancellationToken = default)
    {
        if (editorialArticles is null)
        {
            return publishedItems.Select(item => ArticleFeedKey.Archive(item.Id, item.PublishedAt)).ToList();
        }

        var overlays = await editorialArticles.GetAllLegacyOverlaysAsync(cancellationToken);
        return publishedItems
            .Where(item => !overlays.TryGetValue(item.Id, out var edit) || edit.Status != EditorialArticleStatus.Unpublished)
            .Select(item => ArticleFeedKey.Archive(
                item.Id,
                overlays.TryGetValue(item.Id, out var edit) ? edit.PublishedAt.UtcDateTime : item.PublishedAt))
            .ToList();
    }

    public async Task<IReadOnlyList<ArticleItem>> GetPublishedByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var set = ids.ToHashSet();
        return await ApplyAsync(publishedItems.Where(item => set.Contains(item.Id)).Select(item => item with { Body = string.Empty }).ToList(), cancellationToken);
    }

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
