using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Public article archive reads against legacy tables via EF Core SQL queries.
/// </summary>
public sealed class EfArticlesRepository : IArticlesRepository
{
    private const int MaxPageSize = 100;
    private const int MaxLatestCount = 100;

    private readonly QueenZoneDbContext dbContext;
    private readonly string latestSql;
    private readonly string countSql;
    private readonly string archivePageSql;
    private readonly string byIdSql;
    private readonly string sitemapSql;
    private readonly string feedKeysSql;
    private readonly string byIdsSql;
    private readonly IEditorialArticleRepository? editorialArticles;

    public EfArticlesRepository(QueenZoneDbContext dbContext, IEditorialArticleRepository editorialArticles)
    {
        this.dbContext = dbContext;
        this.editorialArticles = editorialArticles;
        (latestSql, countSql, archivePageSql, byIdSql, sitemapSql, feedKeysSql, byIdsSql) = EfProductionSql.CreateArticlesQueries();
    }

    /// <summary>
    /// Test constructor: SQL templates must use EF <c>{0}</c>/<c>{1}</c> placeholders for
    /// dynamic ints (same as production <see cref="EfProductionSql"/>).
    /// </summary>
    internal EfArticlesRepository(
        QueenZoneDbContext dbContext,
        ArticleRepositorySqlTemplates templates,
        IEditorialArticleRepository? editorialArticles = null)
    {
        this.dbContext = dbContext;
        this.latestSql = templates.LatestSql;
        this.countSql = templates.CountSql;
        this.archivePageSql = templates.ArchivePageSql;
        this.byIdSql = templates.ByIdSql;
        this.sitemapSql = templates.SitemapSql;
        this.feedKeysSql = templates.FeedKeysSql;
        this.byIdsSql = templates.ByIdsSql;
        this.editorialArticles = editorialArticles;
    }

    public async Task<IReadOnlyList<ArticleItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, MaxLatestCount);
        var rows = await dbContext.Database
            .SqlQueryRaw<ArticleRow>(latestSql, take)
            .ToListAsync(cancellationToken);
        return await ApplyOverlaysAsync(rows.Select(MapList).ToList(), cancellationToken);
    }

    public async Task<IReadOnlyList<ArticleFeedKey>> GetPublishedFeedKeysAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<ArticleKeyRow>(feedKeysSql)
            .ToListAsync(cancellationToken);
        if (editorialArticles is null)
        {
            return rows.Select(row => ArticleFeedKey.Archive(row.Id, row.PublishedAt)).ToList();
        }

        var overlays = await editorialArticles.GetAllLegacyOverlaysAsync(cancellationToken);
        return rows
            .Where(row => !overlays.TryGetValue(row.Id, out var edit) || edit.Status != EditorialArticleStatus.Unpublished)
            .Select(row => ArticleFeedKey.Archive(
                row.Id,
                overlays.TryGetValue(row.Id, out var edit) ? edit.PublishedAt.UtcDateTime : row.PublishedAt))
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

        var distinctIds = ids.Distinct().ToArray();
        var sql = ExpandArticleIdEqualityToInList(byIdsSql, distinctIds.Length);
        var parameters = Array.ConvertAll(distinctIds, static id => (object)id);
        var rows = await dbContext.Database
            .SqlQueryRaw<ArticleRow>(sql, parameters)
            .ToListAsync(cancellationToken);
        return await ApplyOverlaysAsync(rows.Select(MapList).ToList(), cancellationToken);
    }

    public async Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default)
    {
        // Avoid FirstAsync composition over raw SQL (can fail for some SQL Server shapes).
        var values = await dbContext.Database
            .SqlQueryRaw<int>(countSql)
            .ToListAsync(cancellationToken);
        if (editorialArticles is null) return values.FirstOrDefault();
        var hidden = await editorialArticles.GetUnpublishedLegacyOverlayCountAsync(cancellationToken);
        return Math.Max(0, values.FirstOrDefault() - hidden);
    }

    public async Task<IReadOnlyList<ArticleItem>> GetArchivePageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var offset = (normalizedPage - 1) * take;
        var rows = await dbContext.Database
            .SqlQueryRaw<ArticleRow>(archivePageSql, offset, take)
            .ToListAsync(cancellationToken);
        return await ApplyOverlaysAsync(rows.Select(MapList).ToList(), cancellationToken);
    }

    public async Task<ArticleItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<ArticleRow>(byIdSql, id)
            .ToListAsync(cancellationToken);
        var row = rows.FirstOrDefault();
        if (row is null) return null;
        return (await ApplyOverlaysAsync([MapDetail(row)], cancellationToken)).SingleOrDefault();
    }

    public async Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<SitemapContentEntry>(sitemapSql)
            .ToListAsync(cancellationToken);
        if (editorialArticles is null) return rows;
        var overlays = await editorialArticles.GetPublishedLegacyOverlaysAsync(rows.Select(x => x.Id), cancellationToken);
        return rows.Where(row => !overlays.TryGetValue(row.Id, out var edit) || edit.Status != EditorialArticleStatus.Unpublished)
            .Select(row => overlays.TryGetValue(row.Id, out var edit)
                ? new SitemapContentEntry(row.Id, edit.Title, edit.PublishedAt.UtcDateTime)
                : row).ToList();
    }

    /// <summary>
    /// List/archive mapping: derive excerpt from optional body preview; never keep full body on list items.
    /// Body may be empty when the list SQL omits it entirely.
    /// </summary>
    private static ArticleItem MapList(ArticleRow row) =>
        new(
            row.Id,
            row.Title,
            LegacyArticleText.GetExcerpt(row.Body),
            string.Empty,
            row.PublishedAt,
            row.Source,
            row.CategoryName,
            row.IsPublished);

    private static ArticleItem MapDetail(ArticleRow row) =>
        new(
            row.Id,
            row.Title,
            LegacyArticleText.GetExcerpt(row.Body),
            row.Body,
            row.PublishedAt,
            row.Source,
            row.CategoryName,
            row.IsPublished);

    private async Task<IReadOnlyList<ArticleItem>> ApplyOverlaysAsync(IReadOnlyList<ArticleItem> items, CancellationToken ct)
    {
        if (editorialArticles is null || items.Count == 0) return items;
        var overlays = await editorialArticles.GetPublishedLegacyOverlaysAsync(items.Select(x => x.Id), ct);
        return EditorialArticleOverlay.Apply(items, overlays);
    }

    /// <summary>
    /// Rewrites a single-id <c>Q_ARTICLE_ID = {0}</c> or <c>Id = {0}</c> predicate
    /// into a parameterized IN list so a page of ids is one round trip.
    /// </summary>
    internal static string ExpandArticleIdEqualityToInList(string byIdsSql, int idCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(idCount, 1);
        foreach (var marker in new[] { "a.Q_ARTICLE_ID = {0}", "Id = {0}" })
        {
            var index = byIdsSql.LastIndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            var column = marker[..marker.IndexOf('=')].Trim();
            var placeholders = string.Join(", ", Enumerable.Range(0, idCount).Select(i => "{" + i + "}"));
            return string.Concat(
                byIdsSql[..index],
                column,
                " IN (",
                placeholders,
                ")",
                byIdsSql[(index + marker.Length)..]);
        }

        throw new InvalidOperationException(
            "Article by-ids SQL must contain an 'a.Q_ARTICLE_ID = {0}' or 'Id = {0}' predicate.");
    }

    internal sealed class ArticleKeyRow
    {
        public int Id { get; set; }

        public DateTime PublishedAt { get; set; }
    }

    internal sealed class ArticleRow
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public DateTime PublishedAt { get; set; }

        public string? Source { get; set; }

        public string? CategoryName { get; set; }

        public bool IsPublished { get; set; }
    }
}

internal sealed record ArticleRepositorySqlTemplates(
    string LatestSql,
    string CountSql,
    string ArchivePageSql,
    string ByIdSql,
    string SitemapSql)
{
    public string FeedKeysSql { get; init; } = "";
    public string ByIdsSql { get; init; } = "";
}
