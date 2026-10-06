using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Discography;

public abstract class AdminDiscographyPageModel : PageModel
{
    public const string SectionPath = "/admin/discography";

    public const string MessageKey = "AdminDiscographyMessage";

    public const string MessageKindKey = "AdminDiscographyMessageKind";

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<string> Errors { get; protected set; } = [];

    [BindProperty(Name = "coverFile")]
    public IFormFile? CoverFile { get; set; }

    [BindProperty(Name = "cropX")]
    public int? CropX { get; set; }

    [BindProperty(Name = "cropY")]
    public int? CropY { get; set; }

    [BindProperty(Name = "cropWidth")]
    public int? CropWidth { get; set; }

    [BindProperty(Name = "cropHeight")]
    public int? CropHeight { get; set; }

    public static string AlbumPath(int albumId) => $"{SectionPath}/{albumId}";

    public static string SongPath(int songId) => $"{SectionPath}/songs/{songId}";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }

    protected NewsArticleImageCrop? Crop =>
        CropX is { } x && CropY is { } y && CropWidth is { } width && CropHeight is { } height
            ? new NewsArticleImageCrop(x, y, width, height)
            : null;

    protected void LoadStatus()
    {
        StatusMessage = TempData[MessageKey] as string;
        StatusMessageKind = TempData[MessageKindKey] as string;
    }

    /// <summary>
    /// Runs a write, flashes <paramref name="successMessage"/> (or the
    /// <see cref="InvalidOperationException"/> message) and redirects.
    /// </summary>
    protected async Task<IActionResult> RunAsync(
        Func<Task> write,
        string successMessage,
        string successPath,
        string? errorPath = null)
    {
        try
        {
            await write();
            Flash(successMessage, success: true);
            return Redirect(successPath);
        }
        catch (InvalidOperationException ex)
        {
            Flash(ex.Message, success: false);
            return Redirect(errorPath ?? successPath);
        }
    }

    protected IActionResult? RequireCoverFile(string redirectPath)
    {
        if (CoverFile is { Length: > 0 })
        {
            return null;
        }

        Flash("Choose an image to upload.", success: false);
        return Redirect(redirectPath);
    }

    protected void Flash(string message, bool success)
    {
        TempData[MessageKey] = message;
        TempData[MessageKindKey] = success ? "success" : "error";
    }
}

public sealed class AlbumFormInput
{
    public string Name { get; set; } = string.Empty;

    public int ArtistId { get; set; } = 1;

    public DateTime? ReleaseDate { get; set; }

    public string? GeneralNotes { get; set; }

    public bool IsActive { get; set; }

    public static AlbumFormInput From(AdminAlbum album) =>
        new()
        {
            Name = album.Name,
            ArtistId = album.ArtistId,
            ReleaseDate = album.ReleaseDate,
            GeneralNotes = album.GeneralNotes,
            IsActive = album.IsActive,
        };

    public AdminAlbumInput ToInput() => new(Name ?? string.Empty, ArtistId, ReleaseDate, GeneralNotes, IsActive);
}

public sealed class SongFormInput
{
    public string Title { get; set; } = string.Empty;

    public string? Lyrics { get; set; }

    public string? Notes { get; set; }

    public bool IsSingle { get; set; }

    public int Position { get; set; }

    public static SongFormInput From(AdminAlbumSong song) =>
        new()
        {
            Title = song.Title,
            Lyrics = song.Lyrics,
            Notes = song.Notes,
            IsSingle = song.IsSingle,
            Position = song.TrackNumber,
        };

    public AdminSongInput ToInput() => new(Title ?? string.Empty, Lyrics, Notes, IsSingle);
}

public sealed record TrackPositionOption(int Value, string Label);

/// <summary>Builds the "position" dropdowns for inserting and moving tracks.</summary>
public static class DiscographyTrackPositions
{
    /// <summary>Positions 1..n+1 for a new song; each label names the track it goes before.</summary>
    public static IReadOnlyList<TrackPositionOption> ForInsert(IReadOnlyList<AdminAlbumSong> songs) =>
        songs
            .Select((song, index) => new TrackPositionOption(index + 1, $"{index + 1} — before “{song.Title}”"))
            .Append(new TrackPositionOption(songs.Count + 1, $"{songs.Count + 1} — at the end"))
            .ToList();

    /// <summary>Positions 1..n for an existing song; each label names the current occupant.</summary>
    public static IReadOnlyList<TrackPositionOption> ForMove(IReadOnlyList<AdminAlbumSong> songs, int songId) =>
        songs
            .Select((song, index) => new TrackPositionOption(
                index + 1,
                song.SongId == songId ? $"{index + 1} — current position" : $"{index + 1} — where “{song.Title}” is now"))
            .ToList();
}

public sealed class IndexModel(IAdminDiscographyRepository repository) : AdminDiscographyPageModel
{
    public IReadOnlyList<AdminAlbumListItem> Albums { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Albums = await repository.GetAlbumsAsync(cancellationToken);
        LoadStatus();
        ViewData["Title"] = "Discography";
        Breadcrumbs = AdminBreadcrumbs.Section("Discography", SectionPath);
    }
}

public sealed class NewModel(IAdminDiscographyRepository repository, AdminDiscographyService service)
    : AdminDiscographyPageModel
{
    [BindProperty]
    public AlbumFormInput AlbumForm { get; set; } = new();

    public IReadOnlyList<AdminArtist> Artists { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await PrepareAsync(cancellationToken);
        LoadStatus();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var input = AlbumForm.ToInput();
        Errors = AdminDiscographyValidation.Validate(input);
        if (Errors.Count > 0)
        {
            await PrepareAsync(cancellationToken);
            return Page();
        }

        try
        {
            var albumId = await service.CreateAlbumAsync(input, cancellationToken);
            Flash("Album created. Add its songs below.", success: true);
            return Redirect(AlbumPath(albumId));
        }
        catch (InvalidOperationException ex)
        {
            Errors = [ex.Message];
            await PrepareAsync(cancellationToken);
            return Page();
        }
    }

    private async Task PrepareAsync(CancellationToken cancellationToken)
    {
        Artists = await repository.GetArtistsAsync(cancellationToken);
        ViewData["Title"] = "Add album";
        Breadcrumbs = AdminBreadcrumbs.Page("Discography", SectionPath, "Add album");
    }
}

public sealed class AlbumModel(IAdminDiscographyRepository repository, AdminDiscographyService service)
    : AdminDiscographyPageModel
{
    public AdminAlbum Album { get; private set; } = null!;

    public IReadOnlyList<AdminArtist> Artists { get; private set; } = [];

    public IReadOnlyList<TrackPositionOption> InsertPositions { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    [BindProperty]
    public AlbumFormInput AlbumForm { get; set; } = new();

    [BindProperty]
    public SongFormInput NewSong { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        AlbumForm = AlbumFormInput.From(Album);
        NewSong = new SongFormInput { Position = Album.Songs.Count + 1 };
        LoadStatus();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id, CancellationToken cancellationToken)
    {
        var input = AlbumForm.ToInput();
        Errors = AdminDiscographyValidation.Validate(input);
        if (Errors.Count > 0)
        {
            return await RedisplayAsync(id, keepAlbumForm: true, cancellationToken);
        }

        return await RunAsync(
            () => service.UpdateAlbumAsync(id, input, cancellationToken),
            "Album saved.",
            AlbumPath(id));
    }

    public async Task<IActionResult> OnPostAddSongAsync(int id, CancellationToken cancellationToken)
    {
        var input = NewSong.ToInput();
        Errors = AdminDiscographyValidation.Validate(input);
        if (Errors.Count > 0)
        {
            return await RedisplayAsync(id, keepAlbumForm: false, cancellationToken);
        }

        return await RunAsync(
            () => service.CreateSongAsync(id, input, NewSong.Position, cancellationToken),
            $"Added “{input.Title.Trim()}”.",
            AlbumPath(id) + "#tracklist");
    }

    public Task<IActionResult> OnPostMoveSongAsync(int id, int songId, int position, CancellationToken cancellationToken) =>
        RunAsync(
            () => service.MoveSongAsync(songId, position, cancellationToken),
            "Track order updated.",
            AlbumPath(id) + "#tracklist");

    public Task<IActionResult> OnPostDeleteSongAsync(int id, int songId, CancellationToken cancellationToken) =>
        RunAsync(
            () => service.DeleteSongAsync(songId, cancellationToken),
            "Song deleted.",
            AlbumPath(id) + "#tracklist");

    public async Task<IActionResult> OnPostCoverAsync(int id, CancellationToken cancellationToken) =>
        RequireCoverFile(AlbumPath(id))
        ?? await RunAsync(
            () => service.SetAlbumCoverAsync(id, CoverFile!, Crop, cancellationToken),
            "Album cover updated.",
            AlbumPath(id));

    public Task<IActionResult> OnPostRemoveCoverAsync(int id, CancellationToken cancellationToken) =>
        RunAsync(
            () => service.RemoveAlbumCoverAsync(id, cancellationToken),
            "Album cover removed.",
            AlbumPath(id));

    public Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken) =>
        RunAsync(
            () => service.DeleteAlbumAsync(id, cancellationToken),
            "Album and its songs deleted.",
            SectionPath,
            AlbumPath(id));

    private async Task<IActionResult> RedisplayAsync(int id, bool keepAlbumForm, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (!keepAlbumForm)
        {
            AlbumForm = AlbumFormInput.From(Album);
        }

        return Page();
    }

    private async Task<bool> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var album = await repository.GetAlbumAsync(id, cancellationToken);
        if (album is null)
        {
            return false;
        }

        Album = album;
        Artists = await repository.GetArtistsAsync(cancellationToken);
        InsertPositions = DiscographyTrackPositions.ForInsert(album.Songs);
        ViewData["Title"] = $"Edit album — {album.Name}";
        Breadcrumbs = AdminBreadcrumbs.Page("Discography", SectionPath, album.Name);
        return true;
    }
}

public sealed class SongModel(IAdminDiscographyRepository repository, AdminDiscographyService service)
    : AdminDiscographyPageModel
{
    public AdminAlbumSong Song { get; private set; } = null!;

    public AdminAlbum Album { get; private set; } = null!;

    public IReadOnlyList<TrackPositionOption> MovePositions { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    [BindProperty]
    public SongFormInput SongForm { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        SongForm = SongFormInput.From(Song);
        LoadStatus();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var input = SongForm.ToInput();
        Errors = AdminDiscographyValidation.Validate(input);
        if (Errors.Count > 0)
        {
            return Page();
        }

        int? position = SongForm.Position != Song.TrackNumber ? SongForm.Position : null;
        return await RunAsync(
            () => service.UpdateSongAsync(id, input, position, cancellationToken),
            "Song saved.",
            SongPath(id));
    }

    public async Task<IActionResult> OnPostCoverAsync(int id, CancellationToken cancellationToken) =>
        RequireCoverFile(SongPath(id))
        ?? await RunAsync(
            () => service.SetSongCoverAsync(id, CoverFile!, Crop, cancellationToken),
            "Single cover updated.",
            SongPath(id));

    public Task<IActionResult> OnPostRemoveCoverAsync(int id, CancellationToken cancellationToken) =>
        RunAsync(
            () => service.RemoveSongCoverAsync(id, cancellationToken),
            "Single cover removed.",
            SongPath(id));

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        var song = await repository.GetSongAsync(id, cancellationToken);
        if (song is null)
        {
            return NotFound();
        }

        return await RunAsync(
            () => service.DeleteSongAsync(id, cancellationToken),
            $"Deleted “{song.Title}”.",
            AlbumPath(song.AlbumId) + "#tracklist",
            SongPath(id));
    }

    private async Task<bool> LoadAsync(int id, CancellationToken cancellationToken)
    {
        var song = await repository.GetSongAsync(id, cancellationToken);
        var album = song is null ? null : await repository.GetAlbumAsync(song.AlbumId, cancellationToken);
        if (song is null || album is null)
        {
            return false;
        }

        Song = song;
        Album = album;
        MovePositions = DiscographyTrackPositions.ForMove(album.Songs, song.SongId);
        ViewData["Title"] = $"Edit song — {song.Title}";
        Breadcrumbs = AdminBreadcrumbs.Page("Discography", SectionPath, album.Name, AlbumPath(album.AlbumId), song.Title);
        return true;
    }
}

/// <summary>Square cover upload form using the shared news crop dialog.</summary>
public sealed record CoverUploadViewModel(
    string Action,
    string RemoveAction,
    string Label,
    string? CurrentUrl,
    string HelpText);

/// <summary>A POST button guarded by the admin confirm dialog (CSP blocks inline confirm()).</summary>
public sealed record DiscographyDeleteViewModel(
    string Action,
    string Label,
    string Question,
    string Detail,
    IReadOnlyDictionary<string, string>? Fields = null);
