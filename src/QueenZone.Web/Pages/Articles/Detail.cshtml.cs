using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Articles;

public sealed class DetailModel(
    IArticlesRepository articlesRepository,
    IOptions<SiteOptions> siteOptions) : PageModel
{
    public ArticleDetailItem? Item { get; private set; }

    public ArticleHeader? Header { get; private set; }

    public string StructuredDataJson { get; private set; } = string.Empty;

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id, string slug, CancellationToken cancellationToken)
    {
        var item = await articlesRepository.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var detail = PublicContentMapper.ToArticleDetailItem(item);
        var canonicalSlug = NewsSlug.Slugify(item.Title);
        if (!string.Equals(canonicalSlug, slug, StringComparison.OrdinalIgnoreCase))
        {
            return RedirectPermanent(detail.DetailPath);
        }

        var canonicalPath = ArticleContent.GetDetailCanonicalPath(detail.Id, detail.Title);

        Item = detail;
        Breadcrumbs = [BreadcrumbItem.Home, new BreadcrumbItem("Articles", "/articles"), new BreadcrumbItem(detail.Title, detail.DetailPath)];
        ViewData["Title"] = $"{detail.Title} | QueenZone articles";
        ViewData["CanonicalPath"] = canonicalPath;
        ViewData["Description"] = detail.Excerpt;

        StructuredDataJson = EditorialJsonLd.BuildArticle(
            detail.Title,
            canonicalPath,
            detail.PublishedAt,
            detail.Excerpt,
            siteOptions.Value.PublicBaseUrl,
            detail.AuthorName);

        Header = new ArticleHeader(detail.Title, detail.ImageUrl, Breadcrumbs,
            string.IsNullOrWhiteSpace(detail.CategoryName) ? "Articles archive" : detail.CategoryName,
            detail.PublishedAt, detail.AuthorName);

        return Page();
    }
}
