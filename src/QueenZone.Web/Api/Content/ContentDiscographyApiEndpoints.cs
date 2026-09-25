using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Studio album list and detail routes.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentDiscographyApiEndpoints
{
    internal static void MapContentDiscographyApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapPagedList<AlbumListItemDto>(
            "/discography",
            GetAlbumsAsync,
            "GetContentDiscographyAlbums",
            "Paged list of studio albums.");

        group.MapDetail<AlbumDetailDto>(
            "/discography/{id:int}",
            GetAlbumDetailAsync,
            "GetContentDiscographyAlbumDetail",
            "A single studio album, with its track list.");
    }

    internal static async Task<IResult> GetAlbumsAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var albums = await publicQueryCache.GetDiscographyAlbumsAsync(cancellationToken);
        return ApiV1EndpointHelpers.OkPagedSlice(
            albums,
            page,
            pageSize,
            ContentApiMapper.ToAlbumListItems);
    }

    internal static async Task<IResult> GetAlbumDetailAsync(
        IDiscographyRepository discographyRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var album = await discographyRepository.GetAlbumByIdAsync(id, cancellationToken);
        if (album is null)
        {
            return ApiV1EndpointHelpers.NotFound($"No album with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToAlbumDetail(album));
    }
}
