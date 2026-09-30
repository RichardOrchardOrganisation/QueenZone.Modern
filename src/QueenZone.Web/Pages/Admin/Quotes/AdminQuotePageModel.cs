using Microsoft.AspNetCore.Mvc.Filters;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Quotes;

public abstract class AdminQuotePageModel : AdminEditPageModel
{
    public const string AntiforgeryTokenFieldName = "__RequestVerificationToken";

    public const string MessageKey = "AdminQuoteMessage";

    public const string MessageKindKey = "AdminQuoteMessageKind";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }

    protected static AdminQuoteDraft ToDraft(QuoteItem quote) =>
        new(quote.Text, quote.WhoSaid, quote.IsPublished, quote.Context);
}
