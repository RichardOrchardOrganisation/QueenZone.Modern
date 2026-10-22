using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web.Pages.Admin.Members;

public sealed class ForumAuthorActionModel(IForumWriteRepository forumWriteRepository, ICoreSitemapService coreSitemapService) : AdminMembersPageModel
{
    [BindProperty]
    public string DisplayName { get; set; } = string.Empty;

    public async Task<IActionResult> OnPostHideAsync(CancellationToken cancellationToken)
    {
        var author = await forumWriteRepository.FindNoAccountForumAuthorAsync(DisplayName, cancellationToken);
        if (author is null) return NotFound();
        await forumWriteRepository.HideAuthorForumContentAsync(null, author.DisplayName, cancellationToken);
        await coreSitemapService.InvalidateAsync(cancellationToken);
        return Redirect($"/admin/members?query={Uri.EscapeDataString(author.DisplayName)}");
    }

    public async Task<IActionResult> OnPostUnhideAsync(CancellationToken cancellationToken)
    {
        var author = await forumWriteRepository.FindNoAccountForumAuthorAsync(DisplayName, cancellationToken);
        if (author is null) return NotFound();
        await forumWriteRepository.UnhideAuthorForumContentAsync(null, author.DisplayName, cancellationToken);
        await coreSitemapService.InvalidateAsync(cancellationToken);
        return Redirect($"/admin/members?query={Uri.EscapeDataString(author.DisplayName)}");
    }
}
