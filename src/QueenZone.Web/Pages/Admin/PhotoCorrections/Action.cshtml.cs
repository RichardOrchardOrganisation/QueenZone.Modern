using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.PhotoCorrections;

public sealed class ActionModel(
    IPhotoCorrectionRepository photoCorrectionRepository,
    PhotoCorrectionReviewService reviewService) : AdminPhotoCorrectionsPageModel
{
    [BindProperty]
    public string? ReviewNotes { get; set; }

    [BindProperty]
    public string? MessageToMember { get; set; }

    public IActionResult OnGet(Guid id) => Redirect($"/admin/photo-corrections/{id}");

    public async Task<IActionResult> OnPostApproveAsync(Guid id, CancellationToken cancellationToken) =>
        Redirect(id, await reviewService.ApproveAsync(id, EditorEmail, ReviewNotes, cancellationToken));

    public async Task<IActionResult> OnPostRevertAsync(Guid id, CancellationToken cancellationToken) =>
        Redirect(id, await reviewService.RevertAsync(id, EditorEmail, ReviewNotes, cancellationToken));

    public async Task<IActionResult> OnPostRejectAsync(Guid id, CancellationToken cancellationToken) =>
        await DecideAsync(
            id,
            () => photoCorrectionRepository.RejectAsync(
                id, EditorEmail, MessageToMember ?? string.Empty, ReviewNotes, cancellationToken),
            "Proposal rejected.");

    public async Task<IActionResult> OnPostRequestInfoAsync(Guid id, CancellationToken cancellationToken) =>
        await DecideAsync(
            id,
            () => photoCorrectionRepository.RequestInfoAsync(
                id, EditorEmail, MessageToMember ?? string.Empty, ReviewNotes, cancellationToken),
            "Question sent. The proposal stays open.");

    private async Task<IActionResult> DecideAsync(
        Guid id,
        Func<Task<PhotoCorrectionProposal?>> decide,
        string successMessage)
    {
        try
        {
            return await decide() is null
                ? NotFound()
                : Redirect(id, new PhotoCorrectionReviewResult(true, successMessage));
        }
        catch (InvalidOperationException ex)
        {
            return Redirect(id, new PhotoCorrectionReviewResult(false, ex.Message));
        }
    }

    private IActionResult Redirect(Guid id, PhotoCorrectionReviewResult result)
    {
        TempData["PhotoCorrectionMessage"] = result.Message;
        TempData["PhotoCorrectionMessageKind"] = result.Succeeded ? "success" : "error";
        return Redirect($"/admin/photo-corrections/{id}");
    }
}
