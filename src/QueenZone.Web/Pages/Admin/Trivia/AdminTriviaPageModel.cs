using Microsoft.AspNetCore.Mvc.Filters;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Trivia;

public abstract class AdminTriviaPageModel : AdminEditPageModel
{
    public const string AntiforgeryTokenFieldName = "__RequestVerificationToken";

    public const string MessageKey = "AdminTriviaMessage";

    public const string MessageKindKey = "AdminTriviaMessageKind";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }

    protected static AdminTriviaDraft ToDraft(TriviaFactItem fact) =>
        new(fact.Text, fact.IsPublished, fact.Category, fact.Difficulty, fact.Source);
}
