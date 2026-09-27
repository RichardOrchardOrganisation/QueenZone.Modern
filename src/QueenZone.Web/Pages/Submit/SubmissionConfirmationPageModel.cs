using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Submit;

public abstract class SubmissionConfirmationPageModel<TSubmission> : PageModel
    where TSubmission : class
{
    public TSubmission? Submission { get; private set; }

    protected async Task<IActionResult> LoadSubmissionAsync(
        Guid id,
        Func<Guid, CancellationToken, Task<TSubmission?>> load,
        Func<TSubmission, Guid> ownerMemberId,
        string title,
        CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        var submission = await load(id, cancellationToken);
        if (submission is null || ownerMemberId(submission) != memberId.Value)
        {
            return NotFound();
        }

        Submission = submission;
        ViewData["Title"] = title;
        return Page();
    }
}
