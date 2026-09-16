using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Quizzes;

public abstract class AdminQuizPageModel : PageModel
{
    public const string AntiforgeryTokenFieldName = "__RequestVerificationToken";

    public const string MessageKey = "AdminQuizMessage";

    public const string MessageKindKey = "AdminQuizMessageKind";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }

    internal static string StatusLabel(QuizAdminItem quiz) =>
        quiz.IsPublished ? "Published" : "Draft";
}
