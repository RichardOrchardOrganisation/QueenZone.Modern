using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class PhotoCorrectionConfirmationModel(IPhotoCorrectionRepository photoCorrectionRepository)
    : SubmissionConfirmationPageModel<PhotoCorrectionProposal>
{
    private const string Title = "Correction sent";

    [BindProperty]
    public string? Reply { get; set; }

    public bool CanReply => Submission?.Status == PhotoCorrectionStatus.NeedsInfo;

    public Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) => LoadAsync(id, cancellationToken);

    public async Task<IActionResult> OnPostReplyAsync(Guid id, CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        try
        {
            if (await photoCorrectionRepository.RespondAsync(id, memberId.Value, Reply ?? string.Empty, cancellationToken) is null)
            {
                return NotFound();
            }
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await LoadAsync(id, cancellationToken);
        }

        return Redirect($"/submit/photo-correction/confirmation/{id:D}");
    }

    public bool CanWithdraw => Submission is not null && PhotoCorrectionStatus.IsOpen(Submission.Status);

    public async Task<IActionResult> OnPostWithdrawAsync(Guid id, CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        try
        {
            if (await photoCorrectionRepository.WithdrawAsync(id, memberId.Value, cancellationToken) is null)
            {
                return NotFound();
            }
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await LoadAsync(id, cancellationToken);
        }

        return Redirect($"/submit/photo-correction/confirmation/{id:D}");
    }

    private Task<IActionResult> LoadAsync(Guid id, CancellationToken cancellationToken) =>
        LoadSubmissionAsync(
            id,
            photoCorrectionRepository.GetByIdAsync,
            proposal => proposal.SubmitterMemberId,
            Title,
            cancellationToken);
}
