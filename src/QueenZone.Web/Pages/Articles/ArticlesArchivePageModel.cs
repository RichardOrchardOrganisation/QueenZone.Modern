using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;
using QueenZone.Web.Archive;

namespace QueenZone.Web.Pages.Articles;

public abstract class ArticlesArchivePageModel(PublicQueryCacheService publicQueryCache) : PageModel
{
    public IReadOnlyList<ArticleArchiveItem> Items { get; private set; } = [];

    public int CurrentPage { get; private set; }

    public int TotalPages { get; private set; }

    public string? ActiveTag { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    protected async Task<IActionResult> LoadArchivePageAsync(
        int page,
        CancellationToken cancellationToken,
        string? tag = null)
    {
        if (page < 1)
        {
            return NotFound();
        }

        var normalizedTag = string.IsNullOrWhiteSpace(tag) ? null : tag;
        var index = await publicQueryCache.GetMergedArticleFeedIndexAsync(normalizedTag, cancellationToken);
        var totalPages = ArticlesRoutes.GetArchiveTotalPages(index.Count);
        if (totalPages == 0)
        {
            if (page > 1)
            {
                return NotFound();
            }
        }
        else if (page > totalPages)
        {
            return NotFound();
        }

        var slice = index
            .Skip((page - 1) * ArticlesRoutes.ArchivePageSize)
            .Take(ArticlesRoutes.ArchivePageSize)
            .ToList();
        Items = await publicQueryCache.HydrateArticleFeedAsync(slice, cancellationToken);
        CurrentPage = page;
        TotalPages = totalPages;
        ActiveTag = normalizedTag;
        Breadcrumbs = [BreadcrumbItem.Home, new BreadcrumbItem("Articles", "/articles")];

        ViewData["Title"] = ArticlesRoutes.GetArchivePageTitle(page);
        ViewData["Description"] = PageMetaDescription.ForArchiveIndex(
            "In-depth Queen articles and interviews from the Queenzone.com archive.",
            page);
        if (page <= 1)
        {
            ViewData["RssFeedPath"] = ArticlesRoutes.FeedPath;
            ViewData["RssFeedTitle"] = "QueenZone Articles";
        }

        if (normalizedTag is null)
        {
            var ctx = new ArchivePageContext(
                page,
                totalPages,
                ArticlesRoutes.GetArchivePageTitle(page),
                ArticlesRoutes.GetArchiveCanonicalPath(page),
                page > 1 ? ArticlesRoutes.GetArchiveCanonicalPath(page - 1) : null,
                totalPages > 0 && page < totalPages ? ArticlesRoutes.GetArchiveCanonicalPath(page + 1) : null);
            ViewData["CanonicalPath"] = ctx.CanonicalPath;
            if (ctx.PrevPath is not null)
            {
                ViewData["PrevPath"] = ctx.PrevPath;
            }

            if (ctx.NextPath is not null)
            {
                ViewData["NextPath"] = ctx.NextPath;
            }
        }
        else
        {
            ViewData["CanonicalPath"] = ArticlesRoutes.GetArchiveCanonicalPath(1);
        }

        return Page();
    }
}
