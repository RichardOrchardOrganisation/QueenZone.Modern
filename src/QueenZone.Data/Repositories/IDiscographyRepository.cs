namespace QueenZone.Data;

public interface IDiscographyRepository
{
    Task<IReadOnlyList<AlbumSummary>> GetAlbumsAsync(CancellationToken cancellationToken = default);

    Task<AlbumDetail?> GetAlbumByIdAsync(int albumId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Canonical songs grouped by <see cref="NewsSlug.Slugify"/> of active-album track titles.
    /// </summary>
    Task<IReadOnlyList<SongSummary>> GetSongsAsync(CancellationToken cancellationToken = default) =>
        SongCatalog.GetSongsAsync(this, cancellationToken);

    Task<SongDetail?> GetSongBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        SongCatalog.GetSongBySlugAsync(this, slug, cancellationToken);
}
