using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Photography;

public sealed class DetailModel(
    PublicQueryCacheService publicQueryCache,
    IPhotoRepository photoRepository,
    IPhotoCorrectionRepository photoCorrectionRepository,
    IPhotoArchiveDatesRepository archiveDatesRepository) : PageModel
{
    public PhotoCategory Category { get; private set; } = null!;

    public PhotoItem Photo { get; private set; } = null!;

    public IReadOnlyList<ArchiveNote> ArchiveNotes { get; private set; } = [];

    public IReadOnlyList<ArchiveNote> Memories { get; private set; } = [];

    /// <summary>When the photo was added to the archive (the upload timestamp), shown apart from any photo or event date.</summary>
    public string? AddedToArchive { get; private set; }

    public int Index { get; private set; }

    public int Count { get; private set; }

    public string? PreviousHref { get; private set; }

    public string? NextHref { get; private set; }

    public string BackToGridHref { get; private set; } = string.Empty;

    public PhotoListFilter SizeFilter { get; private set; } = PhotoListFilter.None;

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = PhotoRoutes.SizeQueryParameter)]
    public string? Size { get; set; }

    /// <summary>
    /// Page and share-preview description. Approved archive notes add their value (never the
    /// contributor's credit, which stays on the page itself).
    /// </summary>
    internal static string BuildDescription(string title, string categoryName, IReadOnlyList<ArchiveNote> notes)
    {
        var description = $"{title} – Queen photography from the {categoryName} collection.";
        return notes.Count == 0
            ? description
            : $"{description} {string.Join(' ', notes.Select(note => $"{note.Label}: {note.Value}."))}";
    }

    /// <summary>
    /// Event date, photograph date and photographer/source as labelled notes. The dates come from the
    /// archive columns; a contributor's credit is added only while it still matches the stored value.
    /// </summary>
    internal static IReadOnlyList<ArchiveNote> BuildArchiveNotes(
        PhotoArchiveDates dates,
        IReadOnlyList<PublicPhotoCorrection> approved)
    {
        var notes = new List<ArchiveNote>();
        if (dates.EventDate is { } eventDate)
        {
            notes.Add(new ArchiveNote("Event date", eventDate.ToDisplay(), CreditFor(approved, PhotoCorrectionKind.EventDate, eventDate)));
        }

        if (dates.PhotoDate is { } photoDate)
        {
            var value = dates.PhotoDateAccuracy == PhotoDateAccuracy.Exact ? photoDate.ToDisplay() : $"c. {photoDate.ToDisplay()}";
            notes.Add(new ArchiveNote("Photographed", value, CreditFor(approved, PhotoCorrectionKind.PhotoDate, photoDate)));
        }

        notes.AddRange(approved
            .Where(correction => correction.Kind == PhotoCorrectionKind.PhotographerSource)
            .Select(ArchiveNote.From));
        return notes;
    }

    private static string? CreditFor(IReadOnlyList<PublicPhotoCorrection> approved, string kind, PartialDate stored) =>
        approved.FirstOrDefault(correction =>
            correction.Kind == kind
            && PartialDate.TryParse(correction.Value, out var proposed)
            && proposed == stored)?.CreditName;

    public async Task<IActionResult> OnGetAsync(string slug, int picId, CancellationToken cancellationToken)
    {
        SizeFilter = PhotoListFilter.Parse(Size);

        var category = await publicQueryCache.GetPhotoCategoryBySlugAsync(slug, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var navigation = await photoRepository.GetDetailNavigationAsync(
            category.CatId,
            picId,
            SizeFilter,
            cancellationToken);
        if (navigation is null)
        {
            return NotFound();
        }

        if (SizeFilter.IsActive && !navigation.MatchedRequestedFilter)
        {
            SizeFilter = PhotoListFilter.None;
        }

        Category = category;
        Photo = navigation.Photo;
        Memories = (await photoCorrectionRepository.GetApprovedMemoriesAsync(picId, cancellationToken))
            .Select(ArchiveNote.From)
            .ToList();
        ArchiveNotes = BuildArchiveNotes(
            await archiveDatesRepository.GetAsync(picId, cancellationToken),
            await photoCorrectionRepository.GetApprovedPublicAsync(picId, cancellationToken));
        AddedToArchive = PartialDate.FromDatabase(Photo.DateTime, PartialDate.PrecisionDay)?.ToDisplay();
        Index = navigation.Index;
        Count = navigation.Count;

        PreviousHref = navigation.PreviousPicId is int previousId
            ? PhotoRoutes.GetDetailPath(category.Slug, previousId, SizeFilter)
            : null;
        NextHref = navigation.NextPicId is int nextId
            ? PhotoRoutes.GetDetailPath(category.Slug, nextId, SizeFilter)
            : null;

        var page = (navigation.Index / PhotoRoutes.CategoryPageSize) + 1;
        BackToGridHref = PhotoRoutes.GetCategoryPagePath(category.Slug, page, SizeFilter);

        Breadcrumbs =
        [
            BreadcrumbItem.Home,
            new BreadcrumbItem("Photography", PhotoRoutes.GetCategoriesPath()),
            new BreadcrumbItem(category.Name, PhotoRoutes.GetCategoryPath(category.Slug, SizeFilter)),
            new BreadcrumbItem(Photo.Title, PhotoRoutes.GetDetailPath(category.Slug, Photo.PicId, SizeFilter)),
        ];

        ViewData["Title"] = $"{Photo.Title} | {category.Name} | Photography | QueenZone";
        ViewData["Description"] = BuildDescription(Photo.Title, category.Name, ArchiveNotes);
        ViewData["CanonicalPath"] = PhotoRoutes.GetDetailPath(category.Slug, Photo.PicId);
        ViewData["OgImage"] = Photo.ImageUrl;
        ViewData["PhotoListFilter"] = SizeFilter;

        return Page();
    }
}

/// <summary>An editor-approved note shown under a photo, with the credit its contributor chose.</summary>
public sealed record ArchiveNote(string Label, string Value, string? Credit)
{
    public string Text => Credit is null ? $"{Label}: {Value}" : $"{Label}: {Value} (thanks to {Credit})";


    public static ArchiveNote From(PublicPhotoCorrection correction) =>
        new(PhotoCorrectionKind.Label(correction.Kind), correction.Value, correction.CreditName);
}
