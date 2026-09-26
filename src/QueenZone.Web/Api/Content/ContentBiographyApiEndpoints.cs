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
        group.MapPagedList<BiographyChapterListItemDto>(
            "/biography",
            GetBiographyChaptersAsync,
            "GetContentBiographyChapters",
            "Paged list of biography chapters, in reading order.");

        group.MapDetail<BiographyChapterDetailDto>(
            "/biography/{id:int}",
            GetBiographyChapterDetailAsync,
            "GetContentBiographyChapterDetail",
            "A single biography chapter, with adjacent-chapter navigation.");
    }

    internal static async Task<IResult> GetBiographyChaptersAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var chapters = BiographyChapterOrdering.ByDisplaySequenceAscending(
            await publicQueryCache.GetBiographyChaptersAsync(cancellationToken));
        return ApiV1EndpointHelpers.OkPagedSlice(
            chapters,
            page,
            pageSize,
            ContentApiMapper.ToBiographyChapterListItems);
    }

    internal static async Task<IResult> GetBiographyChapterDetailAsync(
        IBiographyRepository biographyRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var chapter = await biographyRepository.GetByIdAsync(id, cancellationToken);
        if (chapter is null)
        {
            return ApiV1EndpointHelpers.NotFound($"No biography chapter with id '{id}'.");
        }

        var navigation = await biographyRepository.GetAdjacentChaptersAsync(id, cancellationToken);
        return Results.Ok(ContentApiMapper.ToBiographyChapterDetail(chapter, navigation));
    }
}
