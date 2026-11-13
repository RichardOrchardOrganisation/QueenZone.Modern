using QueenZone.Data;
using QueenZone.Web.Pages;

namespace QueenZone.Web;

/// <summary>
/// Public <c>GET /api/v1/search</c> over the shared <c>SearchDocument</c> index
/// (same <see cref="ISiteSearchService"/> visibility as website <c>/search</c>).
/// </summary>
public static class SearchApiEndpoints
{
    public const string Path = "/api/v1/search";

    public const string TimeoutDetail = "Site search took too long. Try again shortly.";

    public static void MapSearchApiEndpoints(this WebApplication app)
    {
        app.MapGet(Path, SearchAsync)
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Search")
            .WithName("GetSiteSearch")
            .WithSummary("Paged whole-site search against the SearchDocument index. Empty q returns an empty page.")
            .DisableAntiforgery()
            .RequireRateLimiting(QueenZoneRateLimitPolicies.Search)
            .Produces<ApiPagedResponse<SearchResultDto>>()
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);
    }

    internal static async Task<IResult> SearchAsync(
        ISiteSearchService siteSearchService,
        ILoggerFactory loggerFactory,
        string? q,
        string? type,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize, SearchModel.PageSize);
        var contentType = SiteSearchContentType.Normalize(type);
        try
        {
            var results = await siteSearchService.SearchAsync(
                q ?? string.Empty,
                contentType,
                request.Page,
                request.PageSize,
                cancellationToken);

            var response = ApiPagedResponse<SearchResultDto>.Create(
                SearchApiMapper.ToItems(results.Results),
                request.Page,
                request.PageSize,
                results.TotalCount);

            return Results.Ok(response);
        }
        catch (Exception ex) when (ex is SiteSearchTimeoutException || SiteSearchSqlTimeout.IsCommandTimeout(ex))
        {
            if (ex is not SiteSearchTimeoutException)
            {
                SiteSearchSqlTimeout.CreateAndLog(
                    loggerFactory.CreateLogger(nameof(SearchApiEndpoints)),
                    q ?? string.Empty,
                    contentType,
                    request.Page,
                    TimeSpan.Zero,
                    ex);
            }

            return TimeoutProblem();
        }
    }

    internal static IResult TimeoutProblem() =>
        Results.Problem(
            statusCode: StatusCodes.Status504GatewayTimeout,
            title: "Gateway Timeout",
            detail: TimeoutDetail);
}
