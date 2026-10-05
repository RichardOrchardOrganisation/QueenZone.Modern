using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Photography;

public abstract class PhotoCategoryPageModel(PublicQueryCacheService publicQueryCache) : PageModel
{
    public PhotoCategory Category { get; private set; } = null!;

    public IReadOnlyList<PhotoItem> Items { get; private set; } = [];

    public int CurrentPage { get; private set; }

    public int TotalPages { get; private set; }

    public int TotalCount { get; private set; }

    public int RangeStart { get; private set; }

    public int RangeEnd { get; private set; }

    public PhotoListFilter SizeFilter { get; private set; } = PhotoListFilter.None;

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = PhotoRoutes.SizeQueryParameter)]
    public string? Size { get; set; }

    protected async Task<IActionResult> LoadCategoryPageAsync(string slug, int page, CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            return NotFound();
        }

        SizeFilter = PhotoListFilter.Parse(Size);

        var category = await publicQueryCache.GetPhotoCategoryBySlugAsync(slug, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var result = await publicQueryCache.GetPhotoCategoryPageAsync(
            category.CatId,
            page,
            PhotoRoutes.CategoryPageSize,
            SizeFilter,
            cancellationToken);
        var totalPages = PhotoRoutes.GetCategoryTotalPages(result.TotalCount);

        // An empty size filter can be cleared on the current, existing collection page.
        // Still reject URLs beyond the unfiltered collection rather than creating arbitrary pages.
        var lastValidPage = totalPages;
        if (result.TotalCount == 0 && SizeFilter.IsActive)
        {
            lastValidPage = PhotoRoutes.GetCategoryTotalPages(category.ImageCount);
        }

        if (page > Math.Max(1, lastValidPage))
        {
            return NotFound();
        }

        Category = category;
        Items = result.Items;
        CurrentPage = page;
        TotalPages = totalPages;
        TotalCount = result.TotalCount;
        RangeStart = result.TotalCount == 0 ? 0 : ((page - 1) * PhotoRoutes.CategoryPageSize) + 1;
        RangeEnd = result.TotalCount == 0 ? 0 : RangeStart + result.Items.Count - 1;
        Breadcrumbs =
        [
            BreadcrumbItem.Home,
            new BreadcrumbItem("Photography", PhotoRoutes.GetCategoriesPath()),
            new BreadcrumbItem(category.Name, PhotoRoutes.GetCategoryPath(category.Slug, SizeFilter)),
        ];

        var firstPageTitle = SizeFilter.IsActive
            ? $"{category.Name} – {SizeFilter.Label} | Photography | QueenZone"
            : $"{category.Name} | Photography | QueenZone";
        ViewData["Title"] = page <= 1
            ? firstPageTitle
            : $"{category.Name} | Photography – Page {page} | QueenZone";
        if (page <= 1)
        {
            ViewData["Description"] = SizeFilter.IsActive
                ? $"{SizeFilter.Label} photos in the Queen {category.Name} archive on QueenZone."
                : $"Queen {category.Name} photographs from the Queenzone.com archive.";
            if (category.CoverThumbnailUrl is string cover)
            {
                ViewData["OgImage"] = cover;
            }
        }

        ViewData["CanonicalPath"] = PhotoRoutes.GetCategoryPagePath(category.Slug, page, SizeFilter);
        ViewData["PhotoListFilter"] = SizeFilter;

        return Page();
    }
}
