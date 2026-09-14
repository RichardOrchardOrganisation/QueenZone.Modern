using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Forum;

public sealed class ArchiveAuthorModel(
    IMemberAccountRepository memberAccountRepository,
    IForumArchiveAuthorRepository archiveAuthorRepository) : PageModel
{
    public const int PageSize = MemberPublicActivityPresentation.PageSize;

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public ForumArchiveAuthorSummary? Author { get; private set; }

    public IReadOnlyList<MemberActivityViewModel> Posts { get; private set; } = [];

    public ArchivePaginationViewModel? Pagination { get; private set; }

    public async Task<IActionResult> OnGetAsync(int legacyUserId, CancellationToken cancellationToken)
    {
        if (PageNumber < 1)
        {
            return NotFound();
        }

        var linkedAccount = await memberAccountRepository.FindByLinkedLegacyUserIdAsync(legacyUserId, cancellationToken);
        if (linkedAccount is not null)
        {
            return RedirectPermanent($"/members/{linkedAccount.Id}");
        }

        Author = await archiveAuthorRepository.GetSummaryAsync(legacyUserId, cancellationToken);
        if (Author is null)
        {
            return NotFound();
        }

        var postsPage = await archiveAuthorRepository.GetPostsPageAsync(
            legacyUserId,
            PageNumber,
            PageSize,
            Author.PostCount,
            cancellationToken);
        var totalPages = ArchivePagination.GetTotalPages(postsPage.TotalCount, PageSize);
        if (totalPages > 0 && PageNumber > totalPages)
        {
            return NotFound();
        }

        Posts = postsPage.Items.Select(MemberPublicActivityPresentation.ToViewModel).ToList();
        Pagination = ArchivePagination.BuildViewModel(
            "Archive author posts pagination",
            PageNumber,
            totalPages,
            page => page <= 1
                ? $"/forum/archive-authors/{legacyUserId}"
                : $"/forum/archive-authors/{legacyUserId}?pageNumber={page}");
        ViewData["Title"] = Author.DisplayName;
        ViewData["Robots"] = "noindex,follow";
        ViewData["CanonicalPath"] = PageNumber <= 1
            ? $"/forum/archive-authors/{legacyUserId}"
            : $"/forum/archive-authors/{legacyUserId}?pageNumber={PageNumber}";
        return Page();
    }
}
