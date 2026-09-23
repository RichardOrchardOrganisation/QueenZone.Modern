using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Discography;

public sealed class IndexModel(PublicQueryCacheService publicQueryCache) : PageModel
{
    public IReadOnlyList<AlbumSummary> Albums { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } = [BreadcrumbItem.Home, new BreadcrumbItem("Discography", DiscographyRoutes.GetIndexPath())];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Albums = await publicQueryCache.GetDiscographyAlbumsAsync(cancellationToken);
        ViewData["Title"] = "Discography | QueenZone";
        ViewData["Description"] = "Every Queen studio album and release – the complete Queenzone discography.";
        ViewData["CanonicalPath"] = DiscographyRoutes.GetIndexPath();
    }
}
