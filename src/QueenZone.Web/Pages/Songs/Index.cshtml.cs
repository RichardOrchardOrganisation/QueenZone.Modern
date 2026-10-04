using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;
using QueenZone.Routing;

namespace QueenZone.Web.Pages.Songs;

public sealed class IndexModel(PublicQueryCacheService publicQueryCache) : PageModel
{
    public IReadOnlyList<SongSummary> Songs { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; } =
        [BreadcrumbItem.Home, new BreadcrumbItem("Songs", SongRoutes.GetIndexPath())];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Songs = await publicQueryCache.GetSongsAsync(cancellationToken);
        ViewData["Title"] = "Songs | QueenZone";
        ViewData["Description"] = "Every Queen song on one page — lyrics, album appearances, and related archive content.";
        ViewData["CanonicalPath"] = SongRoutes.GetIndexPath();
    }
}
