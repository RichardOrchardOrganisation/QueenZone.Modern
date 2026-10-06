namespace QueenZone.Data;

/// <summary>
/// One tracklist row. <paramref name="CoverUrl"/> is optional single artwork from
/// <c>Q_ALBUM_SONG_T.COVER_URL</c>.
/// </summary>
public sealed record AlbumSong(int SongId, string Title, bool IsSingle, string? Lyrics, string? Notes, string? CoverUrl = null);
