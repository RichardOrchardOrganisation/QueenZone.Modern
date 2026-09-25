using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy)]
public sealed class FollowingModel(
    MemberFollowService memberFollowService,
    IMemberPublicActivityRepository activityRepository,
    IMemberAccountRepository memberAccountRepository,
    PrivateMessageService privateMessageService) : PageModel
{
    public const int FeedPageSize = MemberPublicActivityPresentation.PageSize;

    public const string NotFollowingEmpty =
        "You are not following anyone yet. Follow members from their profiles to see their public contributions here.";

    public const string NoActivityEmpty =
        "People you follow have not published any public contributions yet.";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<MemberActivityViewModel> Activity { get; private set; } = [];

    public ArchivePaginationViewModel? Pagination { get; private set; }

    public string? EmptyMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (PageNumber < 1)
        {
            return NotFound();
        }

        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        var followedIds = (await memberFollowService.ListFollowedIdsAsync(memberId.Value, cancellationToken))
            .Where(id => id != memberId.Value)
            .ToList();
        var authorIds = await FilterVisibleAuthorIdsAsync(memberId.Value, followedIds, cancellationToken);
        if (authorIds.Count == 0)
        {
            EmptyMessage = NotFollowingEmpty;
            ViewData["Title"] = "Following";
            ViewData["CanonicalPath"] = "/following";
            return Page();
        }

        var activityPage = await activityRepository.GetFeedPageAsync(
            authorIds,
            PageNumber,
            FeedPageSize,
            cancellationToken);
        var totalPages = ArchivePagination.GetTotalPages(activityPage.TotalCount, FeedPageSize);
        if (totalPages > 0 && PageNumber > totalPages)
        {
            return NotFound();
        }

        if (activityPage.TotalCount == 0)
        {
            EmptyMessage = NoActivityEmpty;
        }

        Activity = activityPage.Items.Select(MemberPublicActivityPresentation.ToViewModel).ToList();
        Pagination = ArchivePagination.BuildViewModel(
            "Following feed pagination",
            PageNumber,
            totalPages,
            page => page <= 1 ? "/following" : $"/following?pageNumber={page}");
        ViewData["Title"] = "Following";
        ViewData["CanonicalPath"] = PageNumber <= 1
            ? "/following"
            : $"/following?pageNumber={PageNumber}";
        return Page();
    }

    /// <summary>
    /// Two batch queries rather than two per followed member. The follow list is unbounded, so
    /// the previous per-member loop scaled its query count with how many people the viewer
    /// follows.
    /// </summary>
    private async Task<IReadOnlyList<Guid>> FilterVisibleAuthorIdsAsync(
        Guid viewerMemberId,
        IReadOnlyList<Guid> followedIds,
        CancellationToken cancellationToken)
    {
        if (followedIds.Count == 0)
        {
            return [];
        }

        var blocked = await privateMessageService.ListBlockedMemberIdsAsync(
            viewerMemberId,
            followedIds,
            cancellationToken);
        var active = await memberAccountRepository.ListActiveMemberIdsAsync(
            followedIds,
            cancellationToken);

        // Preserve the follow-list order the loop produced.
        return followedIds
            .Where(id => !blocked.Contains(id) && active.Contains(id))
            .ToList();
    }
}
