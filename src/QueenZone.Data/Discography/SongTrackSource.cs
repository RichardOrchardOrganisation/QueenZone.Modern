namespace QueenZone.Data;

/// <summary>
/// One active-album <c>Q_ALBUM_SONG_T</c> row used to compute a canonical song.
/// </summary>
public sealed record SongTrackSource(
    int AlbumSongId,
    string Title,
    string? Lyrics,
    string? Notes,
    bool IsSingle,
    int AlbumId,
    string AlbumName,
    DateTime? AlbumReleaseDate,
    string? CoverUrl = null)
{
    /// <summary>Track-level links for this row.</summary>
    public IReadOnlyList<StreamingLink> StreamingLinks { get; init; } = [];
}
