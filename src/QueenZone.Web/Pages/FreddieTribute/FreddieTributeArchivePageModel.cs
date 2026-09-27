using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.FreddieTribute;

public abstract class FreddieTributeArchivePageModel(
    IFreddieTributeRepository tributeRepository,
    PublicQueryCacheService publicQueryCache) : Microsoft.AspNetCore.Mvc.RazorPages.PageModel
{
    public IReadOnlyList<QueenZone.Data.FreddieTribute> Tributes { get; private set; } = [];

    public QueenZone.Data.FreddieTribute? FeaturedTribute { get; private set; }

    public IReadOnlyList<PhotoItem> FreddiePhotos { get; private set; } = [];

    public int CurrentPage { get; private set; }

    public int TotalPages { get; private set; }

    public int TotalCount { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    protected async Task<IActionResult> LoadArchivePageAsync(int page, CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            return NotFound();
        }

        var archive = await tributeRepository.GetPageAsync(page, FreddieTributeRoutes.PageSize, cancellationToken);
        var totalPages = FreddieTributeRoutes.ResolveTotalPages(
            page,
            archive.Items.Count,
            archive.TotalCount,
            FreddieTributeRoutes.GetTotalPages(archive.TotalCount));

        if (totalPages == 0 ? page > 1 : page > totalPages)
        {
            return NotFound();
        }

        Tributes = archive.Items;
        FeaturedTribute = await publicQueryCache.GetFeaturedFreddieTributeAsync(cancellationToken);
        FreddiePhotos = await publicQueryCache.GetFreddieTributePhotosAsync(cancellationToken);
        CurrentPage = page;
        TotalPages = totalPages;
        TotalCount = archive.TotalCount;
        Breadcrumbs =
        [
            BreadcrumbItem.Home,
            new BreadcrumbItem("Freddie Mercury Tribute", FreddieTributeRoutes.GetIndexPath()),
        ];

        ViewData["Title"] = page <= 1
            ? "Freddie Mercury Tribute | QueenZone"
            : $"Freddie Mercury Tribute - Page {page} | QueenZone";
        ViewData["CanonicalPath"] = FreddieTributeRoutes.GetPagePath(page);
        if (page > 1)
        {
            ViewData["PrevPath"] = FreddieTributeRoutes.GetPagePath(page - 1);
        }

        if (totalPages > 0 && page < totalPages)
        {
            ViewData["NextPath"] = FreddieTributeRoutes.GetPagePath(page + 1);
        }

        return Page();
    }
}
