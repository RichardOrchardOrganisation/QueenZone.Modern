using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Articles;

public sealed class CommunityDetailModel(
    IArticleRepository articleRepository,
    UgcHtml ugcHtml,
    IOptions<SiteOptions> siteOptions) : PageModel
{
    public PublishedArticleSubmission? Item { get; private set; }

    public string FormattedBody { get; private set; } = string.Empty;

    public string? CoverImageUrl { get; private set; }

    public PublishedArticleSubmission? PreviousArticle { get; private set; }

    public PublishedArticleSubmission? NextArticle { get; private set; }

    public ArticleHeader? Header { get; private set; }

    public string StructuredDataJson { get; private set; } = string.Empty;

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string slug, CancellationToken cancellationToken)
    {
        var item = await articleRepository.GetBySlugAsync(slug, cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        Item = item;
        FormattedBody = ugcHtml.FormatForDisplay(item.Body);

        if (!string.IsNullOrWhiteSpace(item.CoverImageBlobPath))
        {
            CoverImageUrl = UgcProxyPaths.GetPath(QueenZone.Storage.BlobUploadContainers.Articles, item.CoverImageBlobPath);
        }

        var (prev, next) = await articleRepository.GetAdjacentAsync(item.PublishedAt, cancellationToken);
        PreviousArticle = prev;
        NextArticle = next;

        var canonicalPath = ArticlesRoutes.GetCommunityArticleDetailPath(item.Slug);

        StructuredDataJson = EditorialJsonLd.BuildArticle(
            item.Title,
            canonicalPath,
            item.PublishedAt.UtcDateTime,
            item.Excerpt,
            siteOptions.Value.PublicBaseUrl,
            item.AuthorDisplayName);

        Breadcrumbs =
        [
            BreadcrumbItem.Home,
            new BreadcrumbItem("Articles", "/articles"),
            new BreadcrumbItem(item.Title, canonicalPath),
        ];

        ViewData["Title"] = $"{item.Title} | QueenZone articles";
        ViewData["CanonicalPath"] = canonicalPath;
        ViewData["Description"] = item.Excerpt;

        Header = new ArticleHeader(item.Title, CoverImageUrl, Breadcrumbs, "Community article",
            item.PublishedAt.DateTime,
            item.AuthorMemberId is not null ? item.AuthorDisplayName : null,
            item.AuthorMemberId, item.ReadTimeMinutes);

        return Page();
    }


}

