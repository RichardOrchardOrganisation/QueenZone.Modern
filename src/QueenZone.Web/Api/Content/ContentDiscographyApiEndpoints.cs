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
        group.MapGet("/discography", GetAlbumsAsync)
            .WithName("GetContentDiscographyAlbums")
            .WithSummary("Paged list of studio albums.")
            .Produces<ApiPagedResponse<AlbumListItemDto>>();

        group.MapGet("/discography/{id:int}", GetAlbumDetailAsync)
            .WithName("GetContentDiscographyAlbumDetail")
            .WithSummary("A single studio album, with its track list.")
            .Produces<AlbumDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> GetAlbumsAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var albums = await publicQueryCache.GetDiscographyAlbumsAsync(cancellationToken);

        var pageItems = albums
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var response = ApiPagedResponse<AlbumListItemDto>.Create(
            ContentApiMapper.ToAlbumListItems(pageItems),
            request.Page,
            request.PageSize,
            albums.Count);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetAlbumDetailAsync(
        IDiscographyRepository discographyRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var album = await discographyRepository.GetAlbumByIdAsync(id, cancellationToken);
        if (album is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No album with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToAlbumDetail(album));
    }
}
