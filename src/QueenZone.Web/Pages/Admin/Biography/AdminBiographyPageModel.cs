using Microsoft.AspNetCore.Mvc.Filters;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Biography;

public abstract class AdminBiographyPageModel : AdminEditPageModel
{
    public const string AntiforgeryTokenFieldName = "__RequestVerificationToken";

    public const string MessageKey = "AdminBiographyMessage";

    public const string MessageKindKey = "AdminBiographyMessageKind";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }

    protected static AdminBiographyDraft ToDraft(BiographyChapterItem chapter) =>
        new(chapter.Title, chapter.Summary, chapter.Body, chapter.DisplaySequence);
}
