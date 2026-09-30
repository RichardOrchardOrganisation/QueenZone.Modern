using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class FanPerformanceConfirmationModel(
    IFanPerformanceSubmissionRepository fanPerformanceSubmissionRepository) : SubmissionConfirmationPageModel<FanPerformanceSubmission>
{
    public Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        LoadSubmissionAsync(
            id,
            fanPerformanceSubmissionRepository.GetByIdAsync,
            submission => submission.SubmitterMemberId,
            "Fan performance submitted",
            cancellationToken);
}
