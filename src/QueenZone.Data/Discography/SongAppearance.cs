namespace QueenZone.Data;

/// <summary>
/// One active-album appearance of a canonical song. Notes, the single flag, and any
/// single cover stay per row.
/// </summary>
public sealed record SongAppearance(
    int AlbumId,
    string AlbumName,
    string AlbumSlug,
    int? ReleaseYear,
    bool IsSingle,
    string? Notes,
    string? CoverUrl = null)
{
    /// <summary>Track-level links for this appearance.</summary>
    public IReadOnlyList<StreamingLink> StreamingLinks { get; init; } = [];
}
