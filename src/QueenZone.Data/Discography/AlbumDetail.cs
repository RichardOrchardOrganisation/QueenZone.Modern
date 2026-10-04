namespace QueenZone.Data;

public sealed record AlbumDetail(
    int AlbumId,
    string Name,
    string Slug,
    int? ReleaseYear,
    string ArtistName,
    string? GeneralNotes,
    string? CoverUrl,
    IReadOnlyList<AlbumSong> Songs,
    DateTime? ReleaseDate = null);
