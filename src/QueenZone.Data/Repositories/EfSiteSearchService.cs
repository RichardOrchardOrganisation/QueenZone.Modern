using Microsoft.Extensions.Logging;

namespace QueenZone.Data;

/// <summary>
/// Calls <c>dbo.SearchDocument_Search</c> for ranked, paginated whole-site search.
/// Passes <see cref="SiteSearchLimits.MaxRankedMatches"/> so common terms stay inside the
/// command timeout. SQL command timeouts are logged at Warning and thrown as
/// <see cref="SiteSearchTimeoutException"/> — the 30-second command timeout is unchanged.
/// </summary>
public sealed class EfSiteSearchService(
    QueenZoneDbContext dbContext,
    ILogger<EfSiteSearchService> logger) : ISiteSearchService
{
    public async Task<SiteSearchPage> SearchAsync(
        string query,
        string? contentType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (SiteSearchLimits.IsBelowMinimumLength(query))
        {
            return new SiteSearchPage([], 0, page, pageSize);
        }

        var normalizedPage = SiteSearchLimits.NormalizePage(page);
        var take = SiteSearchLimits.NormalizePageSize(pageSize);
        var trimmed = query.Trim();

        if (SiteSearchLimits.IsBeyondMaxPage(normalizedPage))
        {
            return await SiteSearchSqlTimeout.ExecuteAsync(
                async ct =>
                {
                    var counted = await ExecuteSearchAsync(
                        trimmed,
                        contentType,
                        offset: 0,
                        take: 1,
                        normalizedPage: 1,
                        ct);
                    return EmptyPageWithTotal(counted.TotalCount, normalizedPage, take);
                },
                logger,
                trimmed,
                cancellationToken);
        }

        var offset = (normalizedPage - 1) * take;
        return await SiteSearchSqlTimeout.ExecuteAsync(
            ct => ExecuteSearchAsync(trimmed, contentType, offset, take, normalizedPage, ct),
            logger,
            trimmed,
            cancellationToken);
    }

    private async Task<SiteSearchPage> ExecuteSearchAsync(
        string query,
        string? contentType,
        int offset,
        int take,
        int normalizedPage,
        CancellationToken cancellationToken)
    {
        var totalRecords = EfSql.OutputInt("@TotalRecords");

        var rows = await EfSql.QueryProcAsync<SiteSearchRow>(
            dbContext,
            "SearchDocument_Search",
            command =>
            {
                command.Parameters.Add(EfSql.Input("@Query", query));
                command.Parameters.Add(EfSql.Input("@ContentType", contentType));
                command.Parameters.Add(EfSql.Input("@Offset", offset));
                command.Parameters.Add(EfSql.Input("@PageSize", take));
                command.Parameters.Add(EfSql.Input("@RankLimit", SiteSearchLimits.MaxRankedMatches));
                command.Parameters.Add(EfSql.Input("@TypedRankLimit", SiteSearchLimits.TypedMatchScanLimit));
                command.Parameters.Add(totalRecords);
            },
            cancellationToken: cancellationToken);

        var results = rows.Select(Map).ToList();
        return new SiteSearchPage(
            results,
            EfSql.GetNullableInt(totalRecords) ?? 0,
            normalizedPage,
            take);
    }

    internal static SiteSearchPage EmptyPageWithTotal(int totalCount, int page, int pageSize) =>
        new([], totalCount, page, pageSize);

    internal static SiteSearchResult Map(SiteSearchRow row) =>
        new(
            row.ContentType,
            row.SourceKey,
            row.Title,
            row.Summary ?? string.Empty,
            row.Url,
            row.PublishedAt,
            row.ImageUrl,
            row.Category,
            row.AuthorDisplayName);

    internal sealed class SiteSearchRow
    {
        public string ContentType { get; set; } = string.Empty;

        public string SourceKey { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string? Summary { get; set; }

        public string Url { get; set; } = string.Empty;

        public DateTimeOffset? PublishedAt { get; set; }

        public string? ImageUrl { get; set; }

        public string? Category { get; set; }

        public string? AuthorDisplayName { get; set; }
    }
}
