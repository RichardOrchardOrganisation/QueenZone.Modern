using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class FanPerformanceConfirmationModel(
    IFanPerformanceSubmissionRepository fanPerformanceSubmissionRepository) : PageModel
{
    public FanPerformanceSubmission? Submission { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        var submission = await fanPerformanceSubmissionRepository.GetByIdAsync(id, cancellationToken);
        if (submission is null || submission.SubmitterMemberId != memberId.Value)
        {
            return NotFound();
        }

        Submission = submission;
        ViewData["Title"] = "Fan performance submitted";
        return Page();
    }
}
