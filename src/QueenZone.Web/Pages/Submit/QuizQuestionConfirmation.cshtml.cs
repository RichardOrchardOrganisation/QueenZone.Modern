using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class QuizQuestionConfirmationModel(IQuizQuestionSubmissionRepository quizQuestionSubmissionRepository) : SubmissionConfirmationPageModel<QuizQuestionSubmission>
{
    public Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        LoadSubmissionAsync(
            id,
            quizQuestionSubmissionRepository.GetByIdAsync,
            submission => submission.SubmitterMemberId,
            "Quiz question submitted",
            cancellationToken);
}
