namespace QueenZone.Data;

public sealed class InMemoryAdminDiscographyRepository(InMemoryDiscographyStore store) : IAdminDiscographyRepository
{
    public Task<IReadOnlyList<AdminAlbumListItem>> GetAlbumsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetAdminAlbums());

    public Task<AdminAlbum?> GetAlbumAsync(int albumId, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetAdminAlbum(albumId));

    public Task<IReadOnlyList<AdminArtist>> GetArtistsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(InMemoryDiscographyStore.Artists);

    public Task<int> CreateAlbumAsync(AdminAlbumInput input, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.CreateAlbum(input));

    public Task UpdateAlbumAsync(int albumId, AdminAlbumInput input, CancellationToken cancellationToken = default)
    {
        store.UpdateAlbum(albumId, input);
        return Task.CompletedTask;
    }

    public Task SetAlbumCoverAsync(int albumId, AdminAlbumCover? cover, CancellationToken cancellationToken = default)
    {
        store.SetAlbumCover(albumId, cover);
        return Task.CompletedTask;
    }

    public Task DeleteAlbumAsync(int albumId, CancellationToken cancellationToken = default)
    {
        store.DeleteAlbum(albumId);
        return Task.CompletedTask;
    }

    public Task<AdminAlbumSong?> GetSongAsync(int songId, CancellationToken cancellationToken = default) =>
        Task.FromResult(store.GetAdminSong(songId));

    public Task<int> CreateSongAsync(
        int albumId,
        AdminSongInput input,
        int position,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(store.CreateSong(albumId, input, position));

    public Task UpdateSongAsync(int songId, AdminSongInput input, CancellationToken cancellationToken = default)
    {
        store.UpdateSong(songId, input);
        return Task.CompletedTask;
    }

    public Task MoveSongAsync(int songId, int position, CancellationToken cancellationToken = default)
    {
        store.MoveSong(songId, position);
        return Task.CompletedTask;
    }

    public Task SetSongCoverAsync(int songId, string? coverFileName, CancellationToken cancellationToken = default)
    {
        store.SetSongCover(songId, coverFileName);
        return Task.CompletedTask;
    }

    public Task DeleteSongAsync(int songId, CancellationToken cancellationToken = default)
    {
        store.DeleteSong(songId);
        return Task.CompletedTask;
    }
}
