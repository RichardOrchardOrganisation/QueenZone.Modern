namespace QueenZone.Data;

public sealed record AdminArtist(int ArtistId, string Name);

public sealed record AdminAlbumListItem(
    int AlbumId,
    string Name,
    DateTime? ReleaseDate,
    bool IsActive,
    string? ThumbFileName,
    int SongCount)
{
    public string? ThumbnailUrl => AlbumCoverUrl.Build(ThumbFileName);
}

/// <summary>
/// Admin view of one <c>Q_ALBUM_T</c> row (active or hidden) with its tracklist.
/// </summary>
public sealed record AdminAlbum(
    int AlbumId,
    string Name,
    int ArtistId,
    DateTime? ReleaseDate,
    string? GeneralNotes,
    bool IsActive,
    string? ThumbFileName,
    string? PictureFileName,
    IReadOnlyList<AdminAlbumSong> Songs)
{
    public string Slug => NewsSlug.Slugify(Name);

    public string? CoverUrl => AlbumCoverUrl.Build(PictureFileName) ?? AlbumCoverUrl.Build(ThumbFileName);

    public AdminAlbumInput ToInput() => new(Name, ArtistId, ReleaseDate, GeneralNotes, IsActive);
}

/// <summary>
/// One <c>Q_ALBUM_SONG_T</c> row. <see cref="TrackNumber"/> is the 1-based position in the
/// album's current order, not necessarily the stored <c>TRACK_NUMBER</c> value.
/// </summary>
public sealed record AdminAlbumSong(
    int SongId,
    int AlbumId,
    int TrackNumber,
    string Title,
    string? Lyrics,
    string? Notes,
    bool IsSingle,
    string? CoverFileName)
{
    public string? CoverUrl => AlbumCoverUrl.Build(CoverFileName);

    public AdminSongInput ToInput() => new(Title, Lyrics, Notes, IsSingle);
}

public sealed record AdminAlbumInput(
    string Name,
    int ArtistId,
    DateTime? ReleaseDate,
    string? GeneralNotes,
    bool IsActive);

public sealed record AdminSongInput(
    string Title,
    string? Lyrics,
    string? Notes,
    bool IsSingle);

/// <summary>
/// Cover filenames (bare names under <c>images/discography</c>) and pixel sizes.
/// </summary>
public sealed record AdminAlbumCover(
    string PictureFileName,
    int PictureWidth,
    int PictureHeight,
    string ThumbFileName,
    int ThumbWidth,
    int ThumbHeight);

/// <summary>
/// Field limits come from the legacy <c>Q_ALBUM_T</c> / <c>Q_ALBUM_SONG_T</c> columns.
/// </summary>
public static class AdminDiscographyValidation
{
    public const int AlbumNameMaxLength = 50;

    public const int GeneralNotesMaxLength = 4000;

    public const int SongTitleMaxLength = 100;

    public const int LyricsMaxLength = 4000;

    public const int SongNotesMaxLength = 2000;

    public const int CoverFileNameMaxLength = 50;

    /// <summary><c>Q_ALBUM_T.Q_ALBUM_ID</c> and <c>ARTIST</c> are <c>tinyint</c>.</summary>
    public const int MaxTinyIntId = 255;

    /// <summary><c>RELEASE_DATE</c> is <c>smalldatetime</c>.</summary>
    public static readonly DateTime MinReleaseDate = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public static readonly DateTime MaxReleaseDate = new(2079, 6, 6, 0, 0, 0, DateTimeKind.Unspecified);

    public static IReadOnlyList<string> Validate(AdminAlbumInput input)
    {
        var errors = new List<string>();
        var name = input.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            errors.Add("Album name is required.");
        }
        else if (name.Length > AlbumNameMaxLength)
        {
            errors.Add($"Album name must be {AlbumNameMaxLength} characters or fewer.");
        }

        if (input.ArtistId < 1 || input.ArtistId > MaxTinyIntId)
        {
            errors.Add("Choose an artist.");
        }

        if (input.ReleaseDate is DateTime date && (date < MinReleaseDate || date >= MaxReleaseDate))
        {
            errors.Add("Release date must be between 1900 and 2079.");
        }

        if ((input.GeneralNotes?.Trim().Length ?? 0) > GeneralNotesMaxLength)
        {
            errors.Add($"Album notes must be {GeneralNotesMaxLength} characters or fewer.");
        }

        return errors;
    }

    public static IReadOnlyList<string> Validate(AdminSongInput input)
    {
        var errors = new List<string>();
        var title = input.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            errors.Add("Song title is required.");
        }
        else if (title.Length > SongTitleMaxLength)
        {
            errors.Add($"Song title must be {SongTitleMaxLength} characters or fewer.");
        }

        if ((input.Lyrics?.Trim().Length ?? 0) > LyricsMaxLength)
        {
            errors.Add($"Lyrics must be {LyricsMaxLength} characters or fewer.");
        }

        if ((input.Notes?.Trim().Length ?? 0) > SongNotesMaxLength)
        {
            errors.Add($"Song notes must be {SongNotesMaxLength} characters or fewer.");
        }

        return errors;
    }

    /// <summary>
    /// Clamps a requested 1-based position into <c>1..count+1</c> (append when out of range).
    /// </summary>
    public static int ClampPosition(int requested, int count) =>
        requested < 1 || requested > count + 1 ? count + 1 : requested;

    /// <summary>
    /// Returns <paramref name="orderedIds"/> with <paramref name="id"/> moved to the 1-based
    /// <paramref name="position"/> (clamped to the list). Unknown ids are appended.
    /// </summary>
    public static IReadOnlyList<int> MoveTo(IReadOnlyList<int> orderedIds, int id, int position)
    {
        var list = orderedIds.Where(existing => existing != id).ToList();
        var index = Math.Clamp(position, 1, list.Count + 1) - 1;
        list.Insert(index, id);
        return list;
    }

    internal static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
