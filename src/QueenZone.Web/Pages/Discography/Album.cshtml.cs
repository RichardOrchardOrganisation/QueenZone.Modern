using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Discography;

public sealed class AlbumModel(PublicQueryCacheService publicQueryCache) : PageModel
{
    public AlbumDetail Album { get; private set; } = null!;

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, string slug, CancellationToken cancellationToken)
    {
        var album = await publicQueryCache.GetDiscographyAlbumByIdAsync(id, cancellationToken);
        if (album is null)
        {
            return NotFound();
        }

        if (!string.Equals(album.Slug, slug, StringComparison.OrdinalIgnoreCase))
        {
            return RedirectPermanent(DiscographyRoutes.GetAlbumPath(album.AlbumId, album.Slug));
        }

        Album = album;
        Breadcrumbs = [BreadcrumbItem.Home, new BreadcrumbItem("Discography", DiscographyRoutes.GetIndexPath()), new BreadcrumbItem(album.Name, DiscographyRoutes.GetAlbumPath(album.AlbumId, album.Slug))];
        ViewData["Title"] = $"{album.Name} | Discography | QueenZone";
        ViewData["CanonicalPath"] = DiscographyRoutes.GetAlbumPath(album.AlbumId, album.Slug);
        if (album.GeneralNotes is not null)
        {
            ViewData["Description"] = NewsArticleContent.ToPlainText(album.GeneralNotes);
        }

        return Page();
    }
}
