using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;
using QueenZone.Routing;

namespace QueenZone.Web.Pages.Songs;

public sealed class DetailModel(PublicQueryCacheService publicQueryCache, ISearchIndexService searchIndex) : PageModel
{
    public SongDetail Song { get; private set; } = null!;

    public IReadOnlyList<SongRelatedSection> Related { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        var song = await publicQueryCache.GetSongBySlugAsync(slug, cancellationToken);
        if (song is null)
        {
            return NotFound();
        }

        if (!string.Equals(song.Slug, slug, StringComparison.Ordinal))
        {
            return RedirectPermanent(SongRoutes.GetSongPath(song.Slug));
        }

        Song = song;
        var documents = await searchIndex.FindByExactTitleAsync(song.Title, cancellationToken);
        Related = SongRelatedContent.ForTitle(documents, song.Title);
        Breadcrumbs =
        [
            BreadcrumbItem.Home,
            new BreadcrumbItem("Songs", SongRoutes.GetIndexPath()),
            new BreadcrumbItem(song.Title, SongRoutes.GetSongPath(song.Slug))
        ];
        ViewData["Title"] = $"{song.Title} | Songs | QueenZone";
        ViewData["CanonicalPath"] = SongRoutes.GetSongPath(song.Slug);
        ViewData["Description"] = song.Appearances.Count == 1
            ? $"{song.Title} — from {song.Appearances[0].AlbumName}."
            : $"{song.Title} — {song.Appearances.Count} album appearances.";

        return Page();
    }
}
