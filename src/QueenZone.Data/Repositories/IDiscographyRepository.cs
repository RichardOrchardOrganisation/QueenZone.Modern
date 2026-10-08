namespace QueenZone.Data;

public interface IDiscographyRepository
{
    Task<IReadOnlyList<AlbumSummary>> GetAlbumsAsync(CancellationToken cancellationToken = default);

    Task<AlbumDetail?> GetAlbumByIdAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Canonical songs grouped by <see cref="NewsSlug.Slugify"/> of active-album track titles.
    /// </summary>
    Task<IReadOnlyList<SongSummary>> GetSongsAsync(CancellationToken cancellationToken = default);

    Task<SongDetail?> GetSongBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every active-album track used to build the canonical song catalogue. The default
    /// walks album details; <see cref="EfDiscographyRepository"/> replaces that with one
    /// album-list call plus one set-based tracklist query.
    /// </summary>
    Task<IReadOnlyList<SongTrackSource>> GetActiveAlbumTracksAsync(
        CancellationToken cancellationToken = default) =>
        SongCatalog.LoadTracksAsync(this, cancellationToken);
}
