using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace QueenZone.Data;

/// <summary>
/// Public news archive reads against legacy <c>NEWS_T</c> latest-row projections via EF Core.
/// </summary>
public sealed class EfNewsRepository : INewsRepository
{
    private const int MaxPageSize = 100;
    private const int MaxLatestCount = 100;

    private readonly QueenZoneDbContext dbContext;
    private readonly string latestSql;
    private readonly string countSql;
    private readonly string archivePageSql;
    private readonly string byIdSql;
    private readonly string sitemapSql;
    private readonly string archivePageByDecadeSql;
    private readonly string countByDecadeSql;
    private readonly string archiveYearRangeSql;
    private readonly INewsSuggestionRepository? newsSuggestionRepository;

    public EfNewsRepository(
        QueenZoneDbContext dbContext,
        INewsSuggestionRepository newsSuggestionRepository)
    {
        this.dbContext = dbContext;
        this.newsSuggestionRepository = newsSuggestionRepository;
        var connectionString = dbContext.Database.GetConnectionString()
            ?? throw new InvalidOperationException("QueenZone legacy database connection string is not configured.");
        var columns = LegacyNewsSchema.GetNewsColumnAvailability(connectionString);
        var includeSlug = columns.HasSlugColumn;
        // List/count/sitemap omit ARTICLE; detail still projects full body.
        var listCte = PublishedNewsQuery.BuildPublishedNewsCte(
            includeSlug,
            includeBody: false,
            columns.HasImageBlobKeyColumn,
            columns.HasImageGalleryPicIdColumn,
            columns.HasForumTopicIdColumn);
        var detailCte = PublishedNewsQuery.BuildPublishedNewsCte(
            includeSlug,
            includeBody: true,
            columns.HasImageBlobKeyColumn,
            columns.HasImageGalleryPicIdColumn,
            columns.HasForumTopicIdColumn);
        (latestSql, countSql, archivePageSql, byIdSql, sitemapSql, archivePageByDecadeSql, countByDecadeSql, archiveYearRangeSql) =
            EfProductionSql.CreateNewsQueries(listCte, detailCte);
    }

    /// <summary>
    /// Test constructor: injects SQLite-compatible SQL templates.
    /// Templates must use EF <c>{0}</c>/<c>{1}</c>/<c>{2}</c> placeholders.
    /// </summary>
    internal EfNewsRepository(
        QueenZoneDbContext dbContext,
        string latestSql,
        string countSql,
        string archivePageSql,
        string byIdSql,
        string sitemapSql,
        INewsSuggestionRepository? newsSuggestionRepository = null,
        string archivePageByDecadeSql = "",
        string countByDecadeSql = "",
        string archiveYearRangeSql = "")
    {
        this.dbContext = dbContext;
        this.latestSql = latestSql;
        this.countSql = countSql;
        this.archivePageSql = archivePageSql;
        this.byIdSql = byIdSql;
        this.sitemapSql = sitemapSql;
        this.newsSuggestionRepository = newsSuggestionRepository;
        this.archivePageByDecadeSql = archivePageByDecadeSql;
        this.countByDecadeSql = countByDecadeSql;
        this.archiveYearRangeSql = archiveYearRangeSql;
    }

    public async Task<IReadOnlyList<NewsItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(count, 1, MaxLatestCount);
        var rows = await dbContext.Database
            .SqlQueryRaw<NewsRow>(latestSql, take)
            .ToListAsync(cancellationToken);
        return await AddSubmissionAttributionAsync(rows.Select(Map).ToList(), cancellationToken);
    }

    public async Task<int> GetPublishedCountAsync(
        NewsArchiveFilter filter = default,
        CancellationToken cancellationToken = default)
    {
        // CTE SQL is non-composable; materialize fully instead of FirstAsync (which tries to compose).
        List<int> values;
        if (filter.IsActive)
        {
            var (start, end) = filter.GetDecadeBounds();
            values = await dbContext.Database
                .SqlQueryRaw<int>(countByDecadeSql, start, end)
                .ToListAsync(cancellationToken);
        }
        else
        {
            values = await dbContext.Database
                .SqlQueryRaw<int>(countSql)
                .ToListAsync(cancellationToken);
        }

        return values.FirstOrDefault();
    }

    public async Task<IReadOnlyList<NewsItem>> GetArchivePageAsync(
        int page,
        int pageSize,
        NewsArchiveFilter filter = default,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(page, 1);
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var offset = (normalizedPage - 1) * take;

        List<NewsRow> rows;
        if (filter.IsActive)
        {
            var (start, end) = filter.GetDecadeBounds();
            rows = await dbContext.Database
                .SqlQueryRaw<NewsRow>(archivePageByDecadeSql, start, end, offset, take)
                .ToListAsync(cancellationToken);
        }
        else
        {
            rows = await dbContext.Database
                .SqlQueryRaw<NewsRow>(archivePageSql, offset, take)
                .ToListAsync(cancellationToken);
        }

        return await AddSubmissionAttributionAsync(rows.Select(Map).ToList(), cancellationToken);
    }

    public async Task<NewsArchiveYearRange> GetArchiveYearRangeAsync(CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<NewsDateRangeRow>(archiveYearRangeSql)
            .ToListAsync(cancellationToken);
        var row = rows.FirstOrDefault();
        return new NewsArchiveYearRange(row?.MinPublishedAt?.Year, row?.MaxPublishedAt?.Year);
    }

    public async Task<NewsItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.Database
            .SqlQueryRaw<NewsRow>(byIdSql, id)
            .ToListAsync(cancellationToken);
        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        return (await AddSubmissionAttributionAsync([Map(row)], cancellationToken))[0];
    }

    public async Task<IReadOnlyList<NewsItem>> GetByIdsAsync(
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var distinctIds = ids.Distinct().ToArray();
        var sql = ExpandIdEqualityToInList(byIdSql, distinctIds.Length);
        var parameters = Array.ConvertAll(distinctIds, static id => (object)id);
        var rows = await dbContext.Database
            .SqlQueryRaw<NewsRow>(sql, parameters)
            .ToListAsync(cancellationToken);
        return await AddSubmissionAttributionAsync(rows.Select(Map).ToList(), cancellationToken);
    }

    /// <summary>
    /// Rewrites the single-id <c>Id = {0}</c> predicate into a parameterized
    /// <c>Id IN ({0}, {1}, …)</c> list so a page of ids is one round trip.
    /// </summary>
    internal static string ExpandIdEqualityToInList(string byIdSql, int idCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(idCount, 1);
        const string marker = "Id = {0}";
        var index = byIdSql.LastIndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException(
                "News by-id SQL must contain an 'Id = {0}' predicate to expand into a batch IN list.");
        }

        var placeholders = string.Join(", ", Enumerable.Range(0, idCount).Select(i => "{" + i + "}"));
        return string.Concat(
            byIdSql[..index],
            "Id IN (",
            placeholders,
            ")",
            byIdSql[(index + marker.Length)..]);
    }

    public async Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Database
            .SqlQueryRaw<SitemapContentEntry>(sitemapSql)
            .ToListAsync(cancellationToken);

    public async Task<NewsSearchPage> SearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new NewsSearchPage([], 0, page, pageSize);
        }

        return await ExecuteSearchWithFtsAsync(query.Trim(), page, pageSize, cancellationToken);
    }

    private async Task<NewsSearchPage> ExecuteSearchWithFtsAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(page, 1);
        var take = Math.Clamp(pageSize, 1, MaxPageSize);
        var offset = (normalizedPage - 1) * take;
        var totalRecords = EfSql.OutputInt("@TotalRecords");

        var rows = await EfSql.QueryProcAsync<NewsRow>(
            dbContext,
            "NEWS_T_SearchPublished",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Query", query));
                command.Parameters.Add(EfSql.Input("@Offset", offset));
                command.Parameters.Add(EfSql.Input("@PageSize", take));
                command.Parameters.Add(totalRecords);
            },
            cancellationToken: cancellationToken);

        var items = await AddSubmissionAttributionAsync(rows.Select(Map).ToList(), cancellationToken);
        return new NewsSearchPage(
            items,
            EfSql.GetNullableInt(totalRecords) ?? 0,
            normalizedPage,
            take);
    }

    private async Task<IReadOnlyList<NewsItem>> AddSubmissionAttributionAsync(
        IReadOnlyList<NewsItem> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0 || newsSuggestionRepository is null)
        {
            return items;
        }

        IReadOnlyList<NewsSubmissionAttribution> attributions;
        try
        {
            attributions = await newsSuggestionRepository.GetPromotedAttributionsAsync(
                items.Select(item => item.Id).Distinct().ToArray(),
                cancellationToken);
        }
        catch (DbException)
        {
            return items;
        }

        var byNewsId = attributions.ToDictionary(attribution => attribution.NewsId);
        return items.Select(item => byNewsId.TryGetValue(item.Id, out var attribution)
                ? item with
                {
                    SubmitterMemberId = attribution.MemberId,
                    SubmitterDisplayName = attribution.DisplayName,
                }
                : item)
            .ToList();
    }

    private static NewsItem Map(NewsRow row) =>
        new(
            row.Id,
            row.Title,
            row.Excerpt,
            row.Body,
            row.PublishedAt,
            row.SourceUrl,
            row.IsPublished,
            row.Slug,
            ImageBlobKey: row.ImageBlobKey,
            ImageGalleryPicId: row.ImageGalleryPicId,
            ForumTopicId: row.ForumTopicId);

    internal sealed class NewsRow
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Excerpt { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public DateTime PublishedAt { get; set; }

        public string? SourceUrl { get; set; }

        public bool IsPublished { get; set; }

        public string? Slug { get; set; }

        public string? ImageBlobKey { get; set; }

        public int? ImageGalleryPicId { get; set; }

        public int? ForumTopicId { get; set; }
    }

    internal sealed class NewsDateRangeRow
    {
        public DateTime? MinPublishedAt { get; set; }

        public DateTime? MaxPublishedAt { get; set; }
    }
}
