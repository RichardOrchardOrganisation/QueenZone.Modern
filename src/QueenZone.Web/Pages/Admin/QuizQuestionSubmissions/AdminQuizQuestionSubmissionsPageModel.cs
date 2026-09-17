using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Admin.QuizQuestionSubmissions;

public abstract class AdminQuizQuestionSubmissionsPageModel : PageModel
{
    public const string AntiforgeryTokenFieldName = "__RequestVerificationToken";

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }

    protected string EditorEmail =>
        User.FindFirstValue(ClaimTypes.Email)
        ?? User.FindFirstValue("preferred_username")
        ?? User.Identity?.Name
        ?? "unknown";
}
