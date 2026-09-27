using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class TriviaConfirmationModel(ITriviaFactSubmissionRepository triviaFactSubmissionRepository) : SubmissionConfirmationPageModel<TriviaFactSubmission>
{
    public Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        LoadSubmissionAsync(
            id,
            triviaFactSubmissionRepository.GetByIdAsync,
            submission => submission.SubmitterMemberId,
            "Trivia fact submitted",
            cancellationToken);
}
