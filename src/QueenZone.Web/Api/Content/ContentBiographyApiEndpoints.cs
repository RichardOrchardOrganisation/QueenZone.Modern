using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Biography chapter list and detail routes.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentBiographyApiEndpoints
{
    internal static void MapContentBiographyApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/biography", GetBiographyChaptersAsync)
            .WithName("GetContentBiographyChapters")
            .WithSummary("Paged list of biography chapters, in reading order.")
            .Produces<ApiPagedResponse<BiographyChapterListItemDto>>();

        group.MapGet("/biography/{id:int}", GetBiographyChapterDetailAsync)
            .WithName("GetContentBiographyChapterDetail")
            .WithSummary("A single biography chapter, with adjacent-chapter navigation.")
            .Produces<BiographyChapterDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> GetBiographyChaptersAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var chapters = BiographyChapterOrdering.ByDisplaySequenceAscending(
            await publicQueryCache.GetBiographyChaptersAsync(cancellationToken));

        var pageItems = chapters
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var response = ApiPagedResponse<BiographyChapterListItemDto>.Create(
            ContentApiMapper.ToBiographyChapterListItems(pageItems),
            request.Page,
            request.PageSize,
            chapters.Count);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetBiographyChapterDetailAsync(
        IBiographyRepository biographyRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var chapter = await biographyRepository.GetByIdAsync(id, cancellationToken);
        if (chapter is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No biography chapter with id '{id}'.");
        }

        var navigation = await biographyRepository.GetAdjacentChaptersAsync(id, cancellationToken);
        return Results.Ok(ContentApiMapper.ToBiographyChapterDetail(chapter, navigation));
    }
}
