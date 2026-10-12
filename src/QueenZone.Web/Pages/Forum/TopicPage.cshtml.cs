using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Pages.Forum;

public sealed class TopicPageModel(
    IForumRepository forumRepository,
    ITopicWatchRepository topicWatchRepository,
    IOptions<ForumOptions> forumOptions,
    IOptions<AdminOptions> adminOptions,
    TimeProvider timeProvider,
    IForumPostReportRepository forumPostReportRepository,
    PrivateMessageService privateMessageService,
    IOptions<CookieTempDataProviderOptions> tempDataCookieOptions) : ForumTopicPageModel(
        forumRepository,
        topicWatchRepository,
        forumOptions,
        adminOptions,
        timeProvider,
        forumPostReportRepository,
        privateMessageService,
        tempDataCookieOptions)
{
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; }

    public async Task<IActionResult> OnGetAsync(int topicId, string slug, CancellationToken cancellationToken)
    {
        var result = await LoadTopicPageAsync(topicId, slug, PageNumber, cancellationToken);
        return result is PageResult && PageNumber == 1
            ? RedirectPermanent(ForumRoutes.GetTopicCanonicalPath(Header!))
            : result;
    }
}
