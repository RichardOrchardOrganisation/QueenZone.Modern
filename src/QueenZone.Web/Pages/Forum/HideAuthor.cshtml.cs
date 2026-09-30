using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Forum;

public sealed class HideAuthorModel(
    IForumWriteRepository forumWriteRepository,
    IOptions<AdminOptions> adminOptions) : PageModel
{
    public ForumEditablePost? Post { get; private set; }

    public ForumAuthorContentSummary? Summary { get; private set; }

    public string ReturnPath => Post is null ? "/forum" : ForumRoutes.GetTopicCanonicalPath(
        Post.TopicId, NewsSlug.Slugify(Post.TopicSubject));

    public async Task<IActionResult> OnGetAsync(int postId, CancellationToken cancellationToken)
    {
        var gate = await GateAdminAsync();
        if (gate is not null)
        {
            return gate;
        }

        Post = await forumWriteRepository.GetPostAsync(postId, cancellationToken);
        if (Post is null) return NotFound();
        Summary = await forumWriteRepository.GetAuthorForumContentSummaryAsync(
            Post.AuthorMemberId, Post.AuthorDisplayName, cancellationToken);
        ViewData["Title"] = $"Hide forum content by {Post.AuthorDisplayName}";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int postId, CancellationToken cancellationToken)
    {
        var gate = await GateAdminAsync();
        if (gate is not null)
        {
            return gate;
        }

        var post = await forumWriteRepository.GetPostAsync(postId, cancellationToken);
        if (post is null) return NotFound();
        await forumWriteRepository.HideAuthorForumContentAsync(
            post.AuthorMemberId, post.AuthorDisplayName, cancellationToken);
        return Redirect(ForumRoutes.GetTopicCanonicalPath(
            post.TopicId, NewsSlug.Slugify(post.TopicSubject)));
    }

    private async Task<IActionResult?> GateAdminAsync()
    {
        if (await ForumAdminAccess.IsAdminAsync(HttpContext, adminOptions.Value))
        {
            return null;
        }

        // Signed-in members, including allowlisted addresses, are not forum admins.
        // Anonymous callers are sent to Entra, not the member login page.
        var memberAuth = await HttpContext.AuthenticateMemberAsync();
        if (memberAuth.Succeeded || User.Identity?.IsAuthenticated == true)
        {
            return Forbid();
        }

        return Challenge(AdminAuthenticationSchemes.CompositeScheme);
    }
}
