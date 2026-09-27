using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.OutputCaching;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Polls;

public abstract class AdminPollPageModel : AdminEditPageModel
{
    public const string AntiforgeryTokenFieldName = "__RequestVerificationToken";

    public const string MessageKey = "AdminPollMessage";

    public const string MessageKindKey = "AdminPollMessageKind";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }

    internal static async Task InvalidatePublicHomeCacheAsync(
        IOutputCacheStore outputCacheStore,
        CancellationToken cancellationToken)
    {
        await outputCacheStore.EvictByTagAsync(PublicOutputCachePolicies.PublicHtmlTag, cancellationToken);
    }

    internal static string StatusLabel(HomePollAdminItem poll)
    {
        if (poll.IsCurrent)
        {
            return poll.ClosedAt is null ? "Live" : "Closed";
        }

        return poll.PublishedAt is null ? "Draft" : "Hidden";
    }
}
