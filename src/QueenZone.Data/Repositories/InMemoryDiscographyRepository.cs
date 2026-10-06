namespace QueenZone.Data;

public sealed class InMemoryDiscographyRepository : IDiscographyRepository
{
    private readonly InMemoryDiscographyStore store;

    public InMemoryDiscographyRepository(IReadOnlyList<AlbumSeed> seedAlbums)
        : this(new InMemoryDiscographyStore(seedAlbums))
    {
    }

    public InMemoryDiscographyRepository(InMemoryDiscographyStore store)
    {
        this.store = store;
    }

    public Task<IReadOnlyList<AlbumSummary>> GetAlbumsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetActiveAlbums());

    public Task<AlbumDetail?> GetAlbumByIdAsync(int albumId, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetActiveAlbum(albumId));

    public Task<IReadOnlyList<SongSummary>> GetSongsAsync(CancellationToken cancellationToken = default) =>
        SongCatalog.GetSongsAsync(this, cancellationToken);

    public Task<SongDetail?> GetSongBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        SongCatalog.GetSongBySlugAsync(this, slug, cancellationToken);
}
