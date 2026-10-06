namespace QueenZone.Data;

/// <summary>
/// Canonical song page model. Lyrics are the first non-blank <c>SONG_LYRICS</c> in
/// earliest-album then lowest <c>Q_ALBUM_SONG_ID</c> order; still unformatted here.
/// <paramref name="CoverUrl"/> is the first single cover in the same order.
/// </summary>
public sealed record SongDetail(
    string Slug,
    string Title,
    string? Lyrics,
    IReadOnlyList<SongAppearance> Appearances,
    string? CoverUrl = null);
