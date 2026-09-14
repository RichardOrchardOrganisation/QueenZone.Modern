using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Forum;

public sealed class BlockModel(
    ForumPostReportService reportService,
    PrivateMessageService privateMessageService) : PageModel
{
    [BindProperty]
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(int postId, CancellationToken cancellationToken)
    {
        var auth = await HttpContext.AuthenticateMemberAsync();
        var memberId = ForumMember.GetMemberId(User) ?? (auth.Succeeded ? ForumMember.GetMemberId(auth.Principal) : null);
        if (memberId is null)
        {
            return Challenge(MemberAuthenticationSchemes.MembersCookie);
        }

        var post = await reportService.GetVisiblePostAsync(postId, cancellationToken);
        if (post?.AuthorMemberId is not Guid authorMemberId)
        {
            return NotFound();
        }

        var result = await privateMessageService.BlockAsync(memberId.Value, authorMemberId, cancellationToken);
        if (!result.Succeeded)
        {
            return BadRequest(result.ErrorMessage ?? "Unable to block member.");
        }

        TempData[ReportModel.StatusMessageKey] = "Member blocked. Their forum posts are now collapsed and they can no longer contact you privately.";
        return LocalRedirect(
            Url.IsLocalUrl(ReturnUrl)
                ? ReturnUrl!
                : $"{ForumRoutes.GetTopicCanonicalPath(post.TopicId, post.ThreadTitle)}#post-{postId}");
    }
}
