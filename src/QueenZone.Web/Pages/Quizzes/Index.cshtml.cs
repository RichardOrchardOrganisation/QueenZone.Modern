using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Quizzes;

/// <summary>
/// The public quiz section is the timed Quiz Sprint only. Quiz types and categories still exist
/// for admin, but are not listed publicly, so the old landing page redirects to the Sprint.
/// </summary>
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Quizzes/Sprint");
}
