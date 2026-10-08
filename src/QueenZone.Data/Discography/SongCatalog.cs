namespace QueenZone.Data;

/// <summary>
/// Read-time song identity: one song per <see cref="NewsSlug.Slugify"/> of an active-album
/// track title. No table, no ISRC, and parentheticals stay distinct.
/// </summary>
public static class SongCatalog
{
    public static string SlugFor(string title) => NewsSlug.Slugify(title);

    public static IReadOnlyList<SongTrackSource> TracksFromAlbums(IEnumerable<AlbumDetail> albums) =>
        albums
            .SelectMany(album => album.Songs.Select(song => new SongTrackSource(
                song.SongId,
                song.Title,
                song.Lyrics,
                song.Notes,
                song.IsSingle,
                album.AlbumId,
                album.Name,
                album.ReleaseDate ?? YearStart(album.ReleaseYear),
                song.CoverUrl)
            {
                StreamingLinks = song.StreamingLinks,
            }))
            .ToList();

    public static async Task<IReadOnlyList<SongTrackSource>> LoadTracksAsync(
        IDiscographyRepository repository,
        CancellationToken cancellationToken = default)
    {
        var albums = await repository.GetAlbumsAsync(cancellationToken);
        var details = new List<AlbumDetail>(albums.Count);
        foreach (var album in albums)
        {
            var detail = await repository.GetAlbumByIdAsync(album.AlbumId, cancellationToken);
            if (detail is not null)
            {
                details.Add(detail);
            }
        }

        return TracksFromAlbums(details);
    }

    public static IReadOnlyList<SongSummary> Summaries(IEnumerable<SongTrackSource> tracks) =>
        Groups(tracks)
            .Select(ToSummary)
            .OrderBy(song => song.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(song => song.Slug, StringComparer.Ordinal)
            .ToList();

    public static SongDetail? DetailFor(IEnumerable<SongTrackSource> tracks, string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var group = Groups(tracks)
            .FirstOrDefault(candidate => string.Equals(candidate.Key, slug, StringComparison.OrdinalIgnoreCase));
        return group is null ? null : ToDetail(group);
    }

    public static async Task<IReadOnlyList<SongSummary>> GetSongsAsync(
        IDiscographyRepository repository,
        CancellationToken cancellationToken = default) =>
        Summaries(await repository.GetActiveAlbumTracksAsync(cancellationToken));

    public static async Task<SongDetail?> GetSongBySlugAsync(
        IDiscographyRepository repository,
        string slug,
        CancellationToken cancellationToken = default) =>
        DetailFor(await repository.GetActiveAlbumTracksAsync(cancellationToken), slug);

    private static IEnumerable<IGrouping<string, SongTrackSource>> Groups(IEnumerable<SongTrackSource> tracks) =>
        tracks
            .Where(track => !string.IsNullOrWhiteSpace(track.Title))
            .GroupBy(track => SlugFor(track.Title));

    private static IReadOnlyList<SongTrackSource> Ordered(IEnumerable<SongTrackSource> tracks) =>
        tracks
            .OrderBy(track => track.AlbumReleaseDate ?? DateTime.MaxValue)
            .ThenBy(track => track.AlbumSongId)
            .ToList();

    private static SongSummary ToSummary(IGrouping<string, SongTrackSource> group)
    {
        var ordered = Ordered(group);
        var canonical = ordered[0];
        return new SongSummary(
            Slug: SlugFor(canonical.Title),
            Title: canonical.Title,
            AppearanceCount: group.Count(),
            AlbumNames: ordered
                .Select(track => track.AlbumName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            EarliestReleaseYear: canonical.AlbumReleaseDate?.Year);
    }

    private static SongDetail ToDetail(IGrouping<string, SongTrackSource> group)
    {
        var ordered = Ordered(group);
        var canonical = ordered[0];
        var lyrics = ordered
            .Select(track => track.Lyrics)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var coverUrl = ordered
            .Select(track => track.CoverUrl)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new SongDetail(
            Slug: SlugFor(canonical.Title),
            Title: canonical.Title,
            Lyrics: lyrics,
            Appearances: ordered
                .Select(track => new SongAppearance(
                    track.AlbumId,
                    track.AlbumName,
                    NewsSlug.Slugify(track.AlbumName),
                    track.AlbumReleaseDate?.Year,
                    track.IsSingle,
                    string.IsNullOrWhiteSpace(track.Notes) ? null : track.Notes,
                    track.CoverUrl)
                {
                    StreamingLinks = track.StreamingLinks,
                })
                .ToList(),
            CoverUrl: coverUrl)
        {
            StreamingLinks = EarliestLinkPerProvider(ordered),
        };
    }

    private static IReadOnlyList<StreamingLink> EarliestLinkPerProvider(IReadOnlyList<SongTrackSource> ordered) =>
        StreamingProviders.All
            .Select(provider => ordered
                .SelectMany(track => track.StreamingLinks)
                .FirstOrDefault(link => link.Provider == provider))
            .OfType<StreamingLink>()
            .ToList();

    private static DateTime? YearStart(int? year) =>
        year is int value ? new DateTime(value, 1, 1, 0, 0, 0, DateTimeKind.Unspecified) : null;
}
