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
        group.MapGet("/photos/categories", GetPhotoCategoriesAsync)
            .WithName("GetContentPhotoCategories")
            .WithSummary("Paged list of public photo gallery categories.")
            .Produces<ApiPagedResponse<PhotoCategoryListItemDto>>();

        group.MapGet("/photos/categories/{slug}", GetPhotoCategoryAsync)
            .WithName("GetContentPhotoCategory")
            .WithSummary("A single public photo gallery category.")
            .Produces<PhotoCategoryListItemDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/photos/categories/{slug}/items", GetPhotoCategoryItemsAsync)
            .WithName("GetContentPhotoCategoryItems")
            .WithSummary("Paged photos in a gallery. pageSize defaults and clamps to 24, matching /photography/{slug}.")
            .Produces<ApiPagedResponse<PhotoListItemDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/photos/categories/{slug}/items/{picId:int}", GetPhotoDetailAsync)
            .WithName("GetContentPhotoDetail")
            .WithSummary("A single public photo, with prev/next neighbors matching the website lightbox.")
            .Produces<PhotoDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> GetPhotoCategoriesAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var categories = await publicQueryCache.GetPhotoCategoriesAsync(cancellationToken);

        var pageItems = categories
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var response = ApiPagedResponse<PhotoCategoryListItemDto>.Create(
            ContentApiMapper.ToPhotoCategoryListItems(pageItems),
            request.Page,
            request.PageSize,
            categories.Count);

        return Results.Ok(response);
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

        var response = ApiPagedResponse<PhotoListItemDto>.Create(
            ContentApiMapper.ToPhotoListItems(result.Items, filter),
            request.Page,
            request.PageSize,
            result.TotalCount);

        return Results.Ok(response);
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
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: $"No public photo category with slug '{slug}'.");

    private static IResult PhotoNotFound(string slug, int picId) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: $"No public photo '{picId}' in category '{slug}'.");
}
