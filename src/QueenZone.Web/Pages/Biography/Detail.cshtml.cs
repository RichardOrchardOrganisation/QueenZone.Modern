using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Biography;

public sealed class DetailModel(IBiographyRepository biographyRepository, PublicQueryCacheService publicQueryCache) : PageModel
{
    public BiographyChapterItem? Chapter { get; private set; }

    public BiographyChapterNav Navigation { get; private set; } = new(null, null);

    public int ChapterIndex { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, string slug, CancellationToken cancellationToken)
    {
        var chapter = await biographyRepository.GetByIdAsync(id, cancellationToken);
        if (chapter is null)
        {
            return NotFound();
        }

        var canonicalSlug = NewsSlug.Slugify(chapter.Title);
        if (!string.Equals(canonicalSlug, slug, StringComparison.OrdinalIgnoreCase))
        {
            return RedirectPermanent(BiographyRoutes.GetChapterDetailPath(chapter));
        }

        var chapters = await publicQueryCache.GetBiographyChaptersAsync(cancellationToken);
        var readingOrder = BiographyChapterOrdering.ByDisplaySequenceAscending(chapters);
        ChapterIndex = readingOrder.ToList().FindIndex(item => item.Id == id);

        Chapter = chapter;
        Breadcrumbs = [BreadcrumbItem.Home, new BreadcrumbItem("Biography", "/biography"), new BreadcrumbItem(chapter.Title, BiographyRoutes.GetChapterDetailPath(chapter))];
        Navigation = ChapterIndex < 0
            ? new BiographyChapterNav(null, null)
            : new BiographyChapterNav(
                ChapterIndex > 0 ? readingOrder[ChapterIndex - 1] : null,
                ChapterIndex < readingOrder.Count - 1 ? readingOrder[ChapterIndex + 1] : null);
        ViewData["Title"] = $"{chapter.Title} | QueenZone biography";
        ViewData["CanonicalPath"] = BiographyContent.GetDetailCanonicalPath(chapter);
        ViewData["Description"] = BiographyContent.GetListSummary(chapter);

        return Page();
    }
}
