using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class QuizQuestionConfirmationModel(IQuizQuestionSubmissionRepository quizQuestionSubmissionRepository) : PageModel
{
    public QuizQuestionSubmission? Submission { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var memberId = await GetCurrentMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        var submission = await quizQuestionSubmissionRepository.GetByIdAsync(id, cancellationToken);
        if (submission is null || submission.SubmitterMemberId != memberId.Value)
        {
            return NotFound();
        }

        Submission = submission;
        ViewData["Title"] = "Quiz question submitted";
        return Page();
    }

    private async Task<Guid?> GetCurrentMemberIdAsync()
    {
        var authResult = await HttpContext.AuthenticateMemberAsync();
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            return null;
        }

        var idValue = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idValue, out var id) ? id : null;
    }
}
