using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Articles;

public sealed class IndexModel(IArticleSubmissionRepository articleSubmissionRepository, IEditorialArticleRepository editorialArticles, IArticlesRepository legacyArticles) : AdminArticlesPageModel
{
    public IReadOnlyList<ArticleSubmissionListItem> Submissions { get; private set; } = [];
    public IReadOnlyList<EditorialArticle> EditorialArticles { get; private set; } = [];
    public IReadOnlyList<PublishedArticleSubmission> PublishedSubmissions { get; private set; } = [];
    public IReadOnlyList<ArticleItem> LegacyArticles { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task OnGetAsync(int page = 1, CancellationToken cancellationToken = default)
    {
        Submissions = await articleSubmissionRepository.GetPendingAsync(Math.Max(1, page), 50, cancellationToken);
        EditorialArticles = await editorialArticles.GetAllAsync(cancellationToken);
        // Published member submissions only get an editorial row once an admin clicks "Edit and
        // prepare"; until then they are in neither the pending queue nor the editorial list.
        var promoted = EditorialArticles.Where(x => x.SourceSubmissionId is not null).Select(x => x.SourceSubmissionId!.Value).ToHashSet();
        PublishedSubmissions = (await articleSubmissionRepository.GetPublishedAsync(cancellationToken)).Where(x => !promoted.Contains(x.Id)).ToList();
        LegacyArticles = await LoadAllLegacyArchiveAsync(legacyArticles, cancellationToken);
        ViewData["Title"] = "Articles";
        Breadcrumbs = AdminBreadcrumbs.Section("Articles", "/admin/articles");
    }
}
