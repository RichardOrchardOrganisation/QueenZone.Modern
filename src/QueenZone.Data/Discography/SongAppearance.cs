namespace QueenZone.Data;

/// <summary>
/// One active-album appearance of a canonical song. Notes and the single flag stay per row.
/// </summary>
public sealed record SongAppearance(
    int AlbumId,
    string AlbumName,
    string AlbumSlug,
    int? ReleaseYear,
    bool IsSingle,
    string? Notes);
