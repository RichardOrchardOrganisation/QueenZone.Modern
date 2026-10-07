namespace QueenZone.Data.Entities;

/// <summary>
/// One Spotify or Apple Music link for a legacy album (<see cref="AlbumSongId"/> null) or one of
/// its tracks. There are no foreign keys to <c>Q_ALBUM_T</c> / <c>Q_ALBUM_SONG_T</c>; admin deletes
/// remove matching rows in the same transaction.
/// </summary>
public sealed class DiscographyStreamingLinkEntity
{
    public int Id { get; set; }

    public int AlbumId { get; set; }

    public int? AlbumSongId { get; set; }

    public StreamingProvider Provider { get; set; }

    public string ExternalId { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public StreamingLinkSource Source { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string? UpdatedBy { get; set; }
}
