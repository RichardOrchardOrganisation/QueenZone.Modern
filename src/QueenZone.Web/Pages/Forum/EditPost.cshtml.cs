using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web.Pages.Forum;

public sealed class EditPostModel(
    IForumWriteRepository forumWriteRepository,
    UgcHtml ugcHtml,
    PublicQueryCacheService publicQueryCache,
    IOptions<ForumOptions> forumOptions,
    IOptions<AdminOptions> adminOptions,
    TimeProvider timeProvider) : PageModel
{
    private readonly ForumOptions forumOptions = forumOptions.Value;
    private readonly AdminOptions adminOptions = adminOptions.Value;

    [BindProperty]
    [Required]
    public string Body { get; set; } = string.Empty;

    [BindProperty]
    public DateTimeOffset? ExpectedUpdatedAt { get; set; }

    public ForumEditablePost? Post { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(int postId, CancellationToken cancellationToken)
    {
        var access = await ResolveEditAccessAsync();
        if (access.Denied is not null)
        {
            return access.Denied;
        }

        var post = await forumWriteRepository.GetPostAsync(postId, cancellationToken);
        if (post is null)
        {
            return NotFound();
        }

        var isAdmin = access.IsAdmin;
        var memberId = access.MemberId;
        if (!ForumPostEditRules.CanEdit(
                post.AuthorMemberId,
                memberId,
                isAdmin,
                post.PostedAt,
                forumOptions.PostEditWindowMinutes,
                timeProvider.GetUtcNow()))
        {
            ErrorMessage = ResolveForbiddenMessage(post, memberId ?? Guid.Empty, isAdmin);
            Response.StatusCode = StatusCodes.Status403Forbidden;
            Post = post;
            PopulatePage(post);
            return Page();
        }

        Post = post;
        Body = post.Body;
        PopulatePage(post);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int postId, CancellationToken cancellationToken)
    {
        var access = await ResolveEditAccessAsync();
        if (access.Denied is not null)
        {
            return access.Denied;
        }

        var existing = await forumWriteRepository.GetPostAsync(postId, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        Post = existing;
        PopulatePage(existing);

        var sanitizedBody = ugcHtml.Sanitize(Body);
        if (string.IsNullOrWhiteSpace(sanitizedBody))
        {
            ModelState.AddModelError(nameof(Body), "Body is required.");
            Body = sanitizedBody;
            return Page();
        }

        var isAdmin = access.IsAdmin;
        var memberId = access.MemberId ?? Guid.Empty;
        var result = await forumWriteRepository.UpdatePostAsync(
            postId,
            memberId,
            sanitizedBody,
            isAdmin,
            forumOptions.PostEditWindowMinutes,
            ExpectedUpdatedAt,
            cancellationToken);

        switch (result.Status)
        {
            case ForumPostUpdateStatus.NotFound:
                return NotFound();
            case ForumPostUpdateStatus.Success:
                publicQueryCache.InvalidateForumStatsCache();
                var page = ((existing.PositionInThread - 1) / ForumRoutes.PostsPageSize) + 1;
                var redirectPath = ForumRoutes.GetTopicCanonicalPath(result.TopicId, result.TopicSubject, page);
                return Redirect(redirectPath + $"#post-{postId}");
            case ForumPostUpdateStatus.ConcurrencyConflict:
                ErrorMessage = OptimisticConcurrencyException.UserMessage;
                var current = await forumWriteRepository.GetPostAsync(postId, cancellationToken);
                if (current is not null)
                {
                    Post = current;
                    Body = current.Body;
                    PopulatePage(current);
                }

                break;
            case ForumPostUpdateStatus.EditWindowExpired:
                ErrorMessage = "This post can no longer be edited.";
                break;
            case ForumPostUpdateStatus.EditingDisabled:
                ErrorMessage = "Post editing is currently disabled.";
                break;
            default:
                ErrorMessage = "You do not have permission to edit this post.";
                break;
        }

        Response.StatusCode = StatusCodes.Status403Forbidden;
        Body = sanitizedBody;
        return Page();
    }

    private void PopulatePage(ForumEditablePost post)
    {
        var topicPath = ForumRoutes.GetTopicCanonicalPath(post.TopicId, post.TopicSubject);
        Breadcrumbs =
        [
            BreadcrumbItem.Home,
            new BreadcrumbItem("Forum", "/forum"),
            new BreadcrumbItem(post.TopicSubject, topicPath),
            new BreadcrumbItem("Edit post", $"/forum/post/{post.PostId}/edit"),
        ];
        ViewData["Title"] = "Edit post";
        ViewData["CanonicalPath"] = $"/forum/post/{post.PostId}/edit";
        ViewData["Robots"] = "noindex,nofollow";
    }

    private string ResolveForbiddenMessage(ForumEditablePost post, Guid memberId, bool isAdmin)
    {
        if (!isAdmin && post.AuthorMemberId == memberId && forumOptions.PostEditWindowMinutes == 0)
        {
            return "Post editing is currently disabled.";
        }

        if (!isAdmin
            && post.AuthorMemberId == memberId
            && forumOptions.PostEditWindowMinutes > 0
            && timeProvider.GetUtcNow() > post.PostedAt.AddMinutes(forumOptions.PostEditWindowMinutes))
        {
            return "This post can no longer be edited.";
        }

        return "You do not have permission to edit this post.";
    }

    private async Task<(bool IsAdmin, Guid? MemberId, IActionResult? Denied)> ResolveEditAccessAsync()
    {
        // Admin edit-any follows the Entra/test admin scheme. Own-post edits stay on the member cookie.
        var isAdmin = await ForumAdminAccess.IsAdminAsync(HttpContext, adminOptions);
        var memberAuth = await ResolveMemberAuthAsync();
        var memberId = ForumMember.GetMemberId(memberAuth?.Principal);
        if (!isAdmin && memberId is null)
        {
            return (false, null, Challenge(MemberAuthenticationSchemes.MembersCookie));
        }

        return (isAdmin, memberId, null);
    }

    private async Task<AuthenticateResult?> ResolveMemberAuthAsync()
    {
        var directId = ForumMember.GetMemberId(User);
        if (directId is not null)
        {
            return AuthenticateResult.Success(new AuthenticationTicket(User, MemberAuthenticationSchemes.MembersCookie));
        }

        var memberAuth = await HttpContext.AuthenticateMemberAsync();
        return memberAuth.Succeeded ? memberAuth : null;
    }
}
