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
        group.MapPagedList<ArticleListItemDto>(
            "/articles",
            GetArticlesListAsync,
            "GetContentArticlesList",
            "Paged list of published long-form archive articles. Editorial archive only — not news and not community submissions.");

        group.MapDetail<ArticleDetailDto>(
            "/articles/{id:int}",
            GetArticleDetailAsync,
            "GetContentArticleDetail",
            "A single published long-form archive article.");
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

        return ApiV1EndpointHelpers.OkPaged(
            ContentApiMapper.ToArticleListItems(items),
            request.Page,
            request.PageSize,
            totalCount);
    }

    internal static async Task<IResult> GetArticleDetailAsync(
        IArticlesRepository articlesRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var item = await articlesRepository.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return ApiV1EndpointHelpers.NotFound($"No published article with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToArticleDetail(item));
    }
}
