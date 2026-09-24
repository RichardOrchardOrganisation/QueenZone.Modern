using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Published news list, detail, and year-range routes.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentNewsApiEndpoints
{
    internal static void MapContentNewsApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/news", GetNewsListAsync)
            .WithName("GetContentNewsList")
            .WithSummary("Paged list of published news articles. Optional 'decade' (e.g. 2010) filters server-side to that 10-year span, or 'year' (e.g. 2008) to a single year; 'year' wins if both are given. Out-of-range years are ignored.")
            .Produces<ApiPagedResponse<NewsListItemDto>>();

        group.MapGet("/news/{id:int}", GetNewsDetailAsync)
            .WithName("GetContentNewsDetail")
            .WithSummary("A single published news article.")
            .Produces<NewsDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/news/years", GetNewsYearRangeAsync)
            .WithName("GetContentNewsYearRange")
            .WithSummary("Earliest/latest published years across the news archive, for the year-rail scrubber's tick marks.")
            .Produces<NewsYearRangeDto>();
    }

    internal static async Task<IResult> GetNewsListAsync(
        PublicQueryCacheService publicQueryCache,
        NewsDiscussionComposer newsDiscussion,
        int? page,
        int? pageSize,
        int? decade,
        int? year,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var filter = NewsArchiveFilter.Parse(decade, year);
        var items = await publicQueryCache.GetNewsArchivePageAsync(
            request.Page,
            request.PageSize,
            filter,
            cancellationToken);
        var totalCount = await publicQueryCache.GetNewsPublishedCountAsync(filter, cancellationToken);

        var response = ApiPagedResponse<NewsListItemDto>.Create(
            await newsDiscussion.ToListItemsAsync(items, cancellationToken),
            request.Page,
            request.PageSize,
            totalCount);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetNewsYearRangeAsync(
        INewsRepository newsRepository,
        CancellationToken cancellationToken)
    {
        var range = await newsRepository.GetArchiveYearRangeAsync(cancellationToken);
        return Results.Ok(new NewsYearRangeDto(range.MinYear, range.MaxYear));
    }

    internal static async Task<IResult> GetNewsDetailAsync(
        INewsRepository newsRepository,
        NewsDiscussionComposer newsDiscussion,
        int id,
        CancellationToken cancellationToken)
    {
        var item = await newsRepository.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No published news article with id '{id}'.");
        }

        return Results.Ok(await newsDiscussion.ToDetailAsync(item, cancellationToken));
    }
}
