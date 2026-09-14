using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Forum;

public sealed class ReportModel(ForumPostReportService reportService) : PageModel
{
    public const string StatusMessageKey = "ForumPostReportMessage";

    public ForumReportablePost? Post { get; private set; }

    [BindProperty]
    [Required]
    public string? Category { get; set; }

    [BindProperty]
    [StringLength(ForumPostReportLimits.MaxDetailsLength)]
    public string? Details { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(int postId, CancellationToken cancellationToken)
    {
        var memberId = await GetCurrentMemberIdAsync();
        if (memberId is null)
        {
            return Challenge(MemberAuthenticationSchemes.MembersCookie);
        }

        Post = await reportService.GetVisiblePostAsync(postId, cancellationToken);
        if (Post is null)
        {
            return NotFound();
        }
        if (Post.AuthorMemberId == memberId)
        {
            return BadRequest(ForumPostReportText.CannotReportOwn);
        }

        ViewData["Title"] = "Report forum post";
        ViewData["Robots"] = "noindex, nofollow";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int postId, CancellationToken cancellationToken)
    {
        var memberId = await GetCurrentMemberIdAsync();
        if (memberId is null)
        {
            return Challenge(MemberAuthenticationSchemes.MembersCookie);
        }

        Post = await reportService.GetVisiblePostAsync(postId, cancellationToken);
        if (Post is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await reportService.ReportAsync(memberId.Value, postId, Category, Details, cancellationToken);
        if (!result.Succeeded)
        {
            if (result.ErrorMessage == ForumPostReportText.PostNotFound)
            {
                return NotFound();
            }
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to submit report.");
            return Page();
        }

        TempData[StatusMessageKey] = result.AlreadyReported ? "Report already submitted." : "Report submitted.";
        return LocalRedirect(GetSafeReturnUrl(postId));
    }

    private string GetSafeReturnUrl(int postId) =>
        Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl!
            : $"{ForumRoutes.GetTopicCanonicalPath(Post!.TopicId, Post.ThreadTitle)}#post-{postId}";

    private async Task<Guid?> GetCurrentMemberIdAsync()
    {
        var direct = ForumMember.GetMemberId(User);
        if (direct is not null)
        {
            return direct;
        }
        var auth = await HttpContext.AuthenticateMemberAsync();
        return auth.Succeeded ? ForumMember.GetMemberId(auth.Principal) : null;
    }
}
