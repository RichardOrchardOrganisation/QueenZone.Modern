using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Published long-form archive article routes.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentArticleApiEndpoints
{
    internal static void MapContentArticleApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/articles", GetArticlesListAsync)
            .WithName("GetContentArticlesList")
            .WithSummary("Paged list of published long-form archive articles. Editorial archive only — not news and not community submissions.")
            .Produces<ApiPagedResponse<ArticleListItemDto>>();

        group.MapGet("/articles/{id:int}", GetArticleDetailAsync)
            .WithName("GetContentArticleDetail")
            .WithSummary("A single published long-form archive article.")
            .Produces<ArticleDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> GetArticlesListAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize, ArticlesRoutes.ArchivePageSize);
        var items = await publicQueryCache.GetArticlesArchivePageAsync(
            request.Page,
            request.PageSize,
            cancellationToken);
        var totalCount = await publicQueryCache.GetArticlePublishedCountAsync(cancellationToken);

        var response = ApiPagedResponse<ArticleListItemDto>.Create(
            ContentApiMapper.ToArticleListItems(items),
            request.Page,
            request.PageSize,
            totalCount);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetArticleDetailAsync(
        IArticlesRepository articlesRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var item = await articlesRepository.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No published article with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToArticleDetail(item));
    }
}
