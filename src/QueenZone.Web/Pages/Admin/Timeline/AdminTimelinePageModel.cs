using Microsoft.AspNetCore.Mvc.Filters;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Timeline;

public abstract class AdminTimelinePageModel : AdminEditPageModel
{
    public const string AntiforgeryTokenFieldName = "__RequestVerificationToken";

    public const string MessageKey = "AdminTimelineMessage";

    public const string MessageKindKey = "AdminTimelineMessageKind";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }

    protected static AdminQueenHistoryDraft ToDraft(QueenHistoryEvent historyEvent) =>
        new(
            historyEvent.Title,
            historyEvent.Summary,
            historyEvent.EventDate,
            historyEvent.DatePrecision,
            historyEvent.Category,
            historyEvent.Importance,
            historyEvent.SourceUrl,
            historyEvent.IsPublished);
}
