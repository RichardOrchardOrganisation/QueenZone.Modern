namespace QueenZone.Data;

/// <summary>
/// Admin reads and writes for the legacy discography tables (<c>Q_ALBUM_T</c>,
/// <c>Q_ALBUM_SONG_T</c>). Write methods throw <see cref="InvalidOperationException"/>
/// when the album or song does not exist.
/// </summary>
public interface IAdminDiscographyRepository
{
    Task<IReadOnlyList<AdminAlbumListItem>> GetAlbumsAsync(CancellationToken cancellationToken = default);

    Task<AdminAlbum?> GetAlbumAsync(int albumId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminArtist>> GetArtistsAsync(CancellationToken cancellationToken = default);

    Task<int> CreateAlbumAsync(AdminAlbumInput input, CancellationToken cancellationToken = default);

    Task UpdateAlbumAsync(int albumId, AdminAlbumInput input, CancellationToken cancellationToken = default);

    /// <summary>Sets the album cover, or clears it when <paramref name="cover"/> is null.</summary>
    Task SetAlbumCoverAsync(int albumId, AdminAlbumCover? cover, CancellationToken cancellationToken = default);

    /// <summary>Deletes the album and every song on it.</summary>
    Task DeleteAlbumAsync(int albumId, CancellationToken cancellationToken = default);

    Task<AdminAlbumSong?> GetSongAsync(int songId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a song at the 1-based <paramref name="position"/> and shifts later tracks down.
    /// Out-of-range positions append.
    /// </summary>
    Task<int> CreateSongAsync(int albumId, AdminSongInput input, int position, CancellationToken cancellationToken = default);

    Task UpdateSongAsync(int songId, AdminSongInput input, CancellationToken cancellationToken = default);

    /// <summary>Moves a song to the 1-based <paramref name="position"/> within its album.</summary>
    Task MoveSongAsync(int songId, int position, CancellationToken cancellationToken = default);

    /// <summary>Sets the single cover filename, or clears it when null.</summary>
    Task SetSongCoverAsync(int songId, string? coverFileName, CancellationToken cancellationToken = default);

    /// <summary>Deletes the song and closes the gap in its album's track numbers.</summary>
    Task DeleteSongAsync(int songId, CancellationToken cancellationToken = default);
}
