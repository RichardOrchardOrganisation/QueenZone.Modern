using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class PhotoConfirmationModel(IPhotoSubmissionRepository photoSubmissionRepository) : SubmissionConfirmationPageModel<PhotoSubmission>
{
    public Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        LoadSubmissionAsync(
            id,
            photoSubmissionRepository.GetByIdAsync,
            submission => submission.SubmitterMemberId,
            "Photo submitted",
            cancellationToken);
}
