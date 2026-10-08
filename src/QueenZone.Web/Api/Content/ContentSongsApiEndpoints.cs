using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Canonical song list and detail routes. Additive under <c>/api/v1/content</c>.
/// </summary>
public static class ContentSongsApiEndpoints
{
    internal static void MapContentSongsApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapPagedList<SongListItemDto>(
            "/songs",
            GetSongsAsync,
            "GetContentSongs",
            "Paged list of canonical Queen songs.");

        group.MapDetail<SongDetailDto>(
            "/songs/{slug}",
            GetSongDetailAsync,
            "GetContentSongDetail",
            "A single canonical song, with album appearances and title-matched related content.");
    }

    internal static async Task<IResult> GetSongsAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var songs = await publicQueryCache.GetSongsAsync(cancellationToken);
        return ApiV1EndpointHelpers.OkPagedSlice(
            songs,
            page,
            pageSize,
            ContentApiMapper.ToSongListItems);
    }

    internal static async Task<IResult> GetSongDetailAsync(
        PublicQueryCacheService publicQueryCache,
        ISearchIndexService searchIndex,
        string slug,
        CancellationToken cancellationToken)
    {
        var song = await publicQueryCache.GetSongBySlugAsync(slug, cancellationToken);
        if (song is null)
        {
            return ApiV1EndpointHelpers.NotFound($"No song with slug '{slug}'.");
        }

        var documents = await searchIndex.FindByExactTitleAsync(song.Title, cancellationToken);
        var related = SongRelatedContent.ForTitle(documents, song.Title);
        return Results.Ok(ContentApiMapper.ToSongDetail(song, related));
    }
}
