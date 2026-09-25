using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Public photo gallery category, item, and detail routes.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentPhotoApiEndpoints
{
    internal static void MapContentPhotoApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapPagedList<PhotoCategoryListItemDto>(
            "/photos/categories",
            GetPhotoCategoriesAsync,
            "GetContentPhotoCategories",
            "Paged list of public photo gallery categories.");

        group.MapDetail<PhotoCategoryListItemDto>(
            "/photos/categories/{slug}",
            GetPhotoCategoryAsync,
            "GetContentPhotoCategory",
            "A single public photo gallery category.");

        group.MapPagedList<PhotoListItemDto>(
            "/photos/categories/{slug}/items",
            GetPhotoCategoryItemsAsync,
            "GetContentPhotoCategoryItems",
            "Paged photos in a gallery. pageSize defaults and clamps to 24, matching /photography/{slug}.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDetail<PhotoDetailDto>(
            "/photos/categories/{slug}/items/{picId:int}",
            GetPhotoDetailAsync,
            "GetContentPhotoDetail",
            "A single public photo, with prev/next neighbors matching the website lightbox.");
    }

    internal static async Task<IResult> GetPhotoCategoriesAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var categories = await publicQueryCache.GetPhotoCategoriesAsync(cancellationToken);
        return ApiV1EndpointHelpers.OkPagedSlice(
            categories,
            page,
            pageSize,
            ContentApiMapper.ToPhotoCategoryListItems);
    }

    internal static async Task<IResult> GetPhotoCategoryAsync(
        PublicQueryCacheService publicQueryCache,
        string slug,
        CancellationToken cancellationToken)
    {
        var category = await publicQueryCache.GetPhotoCategoryBySlugAsync(slug, cancellationToken);
        if (category is null)
        {
            return PhotoCategoryNotFound(slug);
        }

        return Results.Ok(ContentApiMapper.ToPhotoCategoryListItem(category));
    }

    internal static async Task<IResult> GetPhotoCategoryItemsAsync(
        PublicQueryCacheService publicQueryCache,
        string slug,
        int? page,
        int? pageSize,
        string? size,
        CancellationToken cancellationToken)
    {
        var category = await publicQueryCache.GetPhotoCategoryBySlugAsync(slug, cancellationToken);
        if (category is null)
        {
            return PhotoCategoryNotFound(slug);
        }

        var request = ApiPagination.Normalize(
            page,
            pageSize,
            PhotoRoutes.CategoryPageSize,
            PhotoRoutes.CategoryPageSize);
        var filter = PhotoListFilter.Parse(size);
        var result = await publicQueryCache.GetPhotoCategoryPageAsync(
            category.CatId,
            request.Page,
            request.PageSize,
            filter,
            cancellationToken);

        return ApiV1EndpointHelpers.OkPaged(
            ContentApiMapper.ToPhotoListItems(result.Items, filter),
            request.Page,
            request.PageSize,
            result.TotalCount);
    }

    internal static async Task<IResult> GetPhotoDetailAsync(
        PublicQueryCacheService publicQueryCache,
        IPhotoRepository photoRepository,
        string slug,
        int picId,
        string? size,
        CancellationToken cancellationToken)
    {
        var category = await publicQueryCache.GetPhotoCategoryBySlugAsync(slug, cancellationToken);
        if (category is null)
        {
            return PhotoCategoryNotFound(slug);
        }

        var filter = PhotoListFilter.Parse(size);
        var navigation = await photoRepository.GetDetailNavigationAsync(
            category.CatId,
            picId,
            filter,
            cancellationToken);
        if (navigation is null)
        {
            return PhotoNotFound(slug, picId);
        }

        if (filter.IsActive && !navigation.MatchedRequestedFilter)
        {
            filter = PhotoListFilter.None;
        }

        return Results.Ok(ContentApiMapper.ToPhotoDetail(category, navigation, filter));
    }

    private static IResult PhotoCategoryNotFound(string slug) =>
        ApiV1EndpointHelpers.NotFound($"No public photo category with slug '{slug}'.");

    private static IResult PhotoNotFound(string slug, int picId) =>
        ApiV1EndpointHelpers.NotFound($"No public photo '{picId}' in category '{slug}'.");
}
