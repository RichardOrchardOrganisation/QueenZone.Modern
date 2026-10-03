using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Pages.Forum;

public abstract class ForumCategoryPageModel(IForumRepository forumRepository) : PageModel
{
    public ForumCategorySummary? Category { get; private set; }

    public IReadOnlyList<ForumThreadSummary> Topics { get; private set; } = [];

    public int CurrentPage { get; private set; }

    public int TotalPages { get; private set; }

    public int TotalTopics { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public bool CanCreateThread { get; private set; }

    protected async Task<IActionResult> LoadCategoryPageAsync(
        int id,
        string slug,
        int page,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            return NotFound();
        }

        var category = await forumRepository.GetCategoryByIdAsync(id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var categoryView = PublicContentMapper.ToForumCategorySummary(category);
        var canonicalSlug = NewsSlug.Slugify(category.Name);
        if (!string.Equals(canonicalSlug, slug, StringComparison.OrdinalIgnoreCase))
        {
            return RedirectPermanent(ForumRoutes.GetCategoryCanonicalPath(categoryView, page));
        }

        var topicsPage = await forumRepository.GetCategoryTopicsPageAsync(
            id,
            page,
            ForumRoutes.TopicsPageSize,
            cancellationToken);

        var totalPages = ForumRoutes.GetTopicsTotalPages(topicsPage.TotalCount, ForumRoutes.TopicsPageSize);
        if (topicsPage.TotalCount == 0)
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

        Category = categoryView;
        Topics = PublicContentMapper.ToForumThreadSummaries(topicsPage.Topics);
        CurrentPage = page;
        TotalPages = totalPages;
        TotalTopics = topicsPage.TotalCount;
        Breadcrumbs = [BreadcrumbItem.Home, new BreadcrumbItem("Forum", "/forum"), new BreadcrumbItem(categoryView.Name, categoryView.DetailPath)];
        CanCreateThread = await IsMemberAuthenticatedAsync();

        ViewData["Title"] = ForumRoutes.GetCategoryPageTitle(categoryView, page);
        ViewData["CanonicalPath"] = ForumRoutes.GetCategoryCanonicalPath(categoryView, page);
        ViewData["Description"] = string.IsNullOrWhiteSpace(categoryView.Description)
            ? $"QueenZone discussions in {categoryView.Name}. Browse freely; sign in to participate."
            : categoryView.Description;

        if (page > 1)
        {
            ViewData["PrevPath"] = ForumRoutes.GetCategoryCanonicalPath(categoryView, page - 1);
        }

        if (totalPages > 0 && page < totalPages)
        {
            ViewData["NextPath"] = ForumRoutes.GetCategoryCanonicalPath(categoryView, page + 1);
        }

        return Page();
    }

    private async Task<bool> IsMemberAuthenticatedAsync() =>
        (await HttpContext.AuthenticateMemberAsync()).Succeeded;
}
