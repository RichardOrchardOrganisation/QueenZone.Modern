namespace QueenZone.Data;

/// <summary>
/// One canonical song, grouped by <see cref="NewsSlug.Slugify"/> of the track title.
/// </summary>
public sealed record SongSummary(
    string Slug,
    string Title,
    int AppearanceCount,
    IReadOnlyList<string> AlbumNames,
    int? EarliestReleaseYear);
