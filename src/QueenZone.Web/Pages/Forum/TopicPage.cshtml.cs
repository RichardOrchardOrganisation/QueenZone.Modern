using Microsoft.AspNetCore.Mvc;
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
    PrivateMessageService privateMessageService) : ForumTopicPageModel(
        forumRepository,
        topicWatchRepository,
        forumOptions,
        adminOptions,
        timeProvider,
        forumPostReportRepository,
        privateMessageService)
{
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; }

    public async Task<IActionResult> OnGetAsync(int topicId, string slug, CancellationToken cancellationToken)
    {
        if (PageNumber == 1)
        {
            return RedirectPermanent(ForumRoutes.GetTopicCanonicalPath(topicId, slug));
        }

        return await LoadTopicPageAsync(topicId, slug, PageNumber, cancellationToken);
    }
}
