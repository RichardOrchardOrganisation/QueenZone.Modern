using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Forum;

/// <summary>
/// Sends a legacy topic or reply ID to the topic page that now holds it. Post bodies link here
/// via <see cref="LegacyQueenZoneLinks"/>. The redirect is temporary because hiding posts can
/// move a reply to a different page.
/// </summary>
public sealed class LegacyPostRedirectModel(IForumRepository forumRepository) : PageModel
{
    public async Task<IActionResult> OnGetAsync(int legacyPostId, CancellationToken cancellationToken)
    {
        var location = await forumRepository.FindLegacyPostAsync(legacyPostId, cancellationToken);
        return location is null
            ? NotFound()
            : Redirect(ForumRoutes.GetLegacyPostTargetPath(location));
    }
}
