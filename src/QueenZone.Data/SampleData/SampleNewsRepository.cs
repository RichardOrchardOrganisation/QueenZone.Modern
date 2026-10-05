namespace QueenZone.Data;

public sealed class SampleNewsRepository : INewsRepository
{
    private static readonly IReadOnlyList<NewsItem> Items = BuildItems();

    private static IReadOnlyList<NewsItem> GetPublishedItems() =>
        Items.Where(item => item.IsPublished).ToList();

    public Task<IReadOnlyList<NewsItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NewsItem>>(GetPublishedItems().Take(count).ToList());

    public Task<IReadOnlyList<NewsItem>> GetArchivePageAsync(
        int page,
        int pageSize,
        NewsArchiveFilter filter = default,
        CancellationToken cancellationToken = default)
    {
        var filtered = NewsArchiveFiltering.Apply(GetPublishedItems(), filter);
        var skip = Math.Max(page - 1, 0) * pageSize;
        return Task.FromResult<IReadOnlyList<NewsItem>>(filtered.Skip(skip).Take(pageSize).ToList());
    }

    public Task<int> GetPublishedCountAsync(NewsArchiveFilter filter = default, CancellationToken cancellationToken = default) =>
        Task.FromResult(NewsArchiveFiltering.Apply(GetPublishedItems(), filter).Count);

    public Task<NewsArchiveYearRange> GetArchiveYearRangeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(NewsArchiveYearRanges.Compute(GetPublishedItems()));

    public Task<NewsItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            NewsItemOrdering.ByCreatedDateDescending(
                    GetPublishedItems().Where(item => item.Id == id))
                .FirstOrDefault());

    public Task<IReadOnlyList<NewsItem>> GetByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<NewsItem>>([]);
        }

        var idSet = ids as ISet<int> ?? ids.ToHashSet();
        return Task.FromResult<IReadOnlyList<NewsItem>>(
            NewsItemOrdering.ByCreatedDateDescending(
                GetPublishedItems().Where(item => idSet.Contains(item.Id))));
    }

    public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SitemapContentEntry>>(GetPublishedItems()
            .Select(item => new SitemapContentEntry(item.Id, item.Title, item.PublishedAt, item.Slug))
            .ToList());

    public Task<NewsSearchPage> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new NewsSearchPage([], 0, page, pageSize));
        }

        var term = query.Trim();
        var matches = GetPublishedItems()
            .Where(item =>
                item.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Excerpt.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                item.Body.Contains(term, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var normalizedPage = Math.Max(page, 1);
        var skip = (normalizedPage - 1) * pageSize;
        var items = matches.Skip(skip).Take(pageSize).ToList();

        return Task.FromResult(new NewsSearchPage(items, matches.Count, normalizedPage, pageSize));
    }

    private static IReadOnlyList<NewsItem> BuildItems()
    {
        var items = new List<NewsItem>
        {
            new(
                1003,
                "QueenZone modernisation begins",
                "The first local vertical slice is now running from the new ASP.NET Core application.",
                """
                <p>The first local vertical slice is now running from the new <strong>ASP.NET Core</strong> application.</p>
                <p>See the <a href="https://www.queenzone.org/news">news archive</a> for updates, and the crest below.</p>
                <p><img src="/ugc/news/sample-crest.jpg" alt="QueenZone crest" loading="lazy"></p>
                """,
                new DateTime(2026, 6, 11, 9, 0, 0, DateTimeKind.Utc),
                null,
                true),
            new(
                1002,
                "Legacy news archive mapped",
                "The first migration target is NEWS_T, with clean canonical routes for the modern archive.",
                "News gives the rebuild a narrow, valuable slice through data access, routing, page rendering, search-friendly URLs, and deployment.",
                new DateTime(2026, 6, 10, 9, 0, 0, DateTimeKind.Utc),
                null,
                true),
            new(
                1001,
                "Read-only launch strategy accepted",
                "The first public release will focus on preserving archive content before restoring interactive features.",
                "Accounts, posting, uploads, private messages, and administration are intentionally out of scope for the first release.",
                new DateTime(2026, 6, 9, 9, 0, 0, DateTimeKind.Utc),
                null,
                true),
            new(
                9001,
                "Hidden moderation draft",
                "This record should never appear in public archive output.",
                "Moderated content remains in the legacy database but is excluded from the modern read-only archive.",
                new DateTime(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc),
                null,
                false)
        };

        for (var id = 1004; id <= 1022; id++)
        {
            var dayOffset = 1022 - id;
            items.Add(new NewsItem(
                id,
                $"Archive sample article {id}",
                $"Excerpt for archive sample article {id}.",
                $"Body for archive sample article {id}.",
                new DateTime(2026, 6, 8, 9, 0, 0, DateTimeKind.Utc).AddDays(-dayOffset),
                null,
                true));
        }

        return NewsItemOrdering.ByCreatedDateDescending(items);
    }
}
