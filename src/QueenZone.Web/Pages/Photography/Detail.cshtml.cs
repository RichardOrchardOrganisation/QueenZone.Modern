using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Photography;

public sealed class DetailModel(
    PublicQueryCacheService publicQueryCache,
    IPhotoRepository photoRepository) : PageModel
{
    public PhotoCategory Category { get; private set; } = null!;

    public PhotoItem Photo { get; private set; } = null!;

    public int Index { get; private set; }

    public int Count { get; private set; }

    public string? PreviousHref { get; private set; }

    public string? NextHref { get; private set; }

    public string BackToGridHref { get; private set; } = string.Empty;

    public PhotoListFilter SizeFilter { get; private set; } = PhotoListFilter.None;

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = PhotoRoutes.SizeQueryParameter)]
    public string? Size { get; set; }

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
        ViewData["Description"] = $"{Photo.Title} – Queen photography from the {category.Name} collection.";
        ViewData["CanonicalPath"] = PhotoRoutes.GetDetailPath(category.Slug, Photo.PicId);
        ViewData["OgImage"] = Photo.ImageUrl;
        ViewData["PhotoListFilter"] = SizeFilter;

        return Page();
    }
}
