using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.HelpRequests;

public sealed class ActionModel(
    IHelpRequestRepository helpRequestRepository,
    HelpRequestReplyService replyService) : AdminHelpRequestsPageModel
{
    [BindProperty]
    public string? Status { get; set; }

    [BindProperty]
    public string? ReviewNotes { get; set; }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Status))
        {
            return RedirectWithMessage(id, "A status is required.", "error");
        }

        try
        {
            var updated = await helpRequestRepository.UpdateStatusAsync(
                id,
                Status,
                EditorEmail,
                ReviewNotes,
                cancellationToken);
            if (updated is null)
            {
                return NotFound();
            }

            TempData["HelpRequestMessage"] = $"Marked as {HelpRequestStatus.DisplayName(updated.Status)}.";
            TempData["HelpRequestMessageKind"] = "success";
        }
        catch (ArgumentException ex)
        {
            TempData["HelpRequestMessage"] = ex.Message;
            TempData["HelpRequestMessageKind"] = "error";
        }

        return Redirect($"/admin/help/{id}");
    }

    [BindProperty]
    public string? ReplySubject { get; set; }

    [BindProperty]
    public string? ReplyBody { get; set; }

    [BindProperty]
    public bool MarkResolved { get; set; }

    public async Task<IActionResult> OnPostReplyAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await replyService.ReplyAsync(
            id,
            ReplySubject,
            ReplyBody,
            EditorEmail,
            MarkResolved,
            cancellationToken);

        switch (result.Outcome)
        {
            case HelpRequestReplyService.ReplyOutcome.NotFound:
                return NotFound();
            case HelpRequestReplyService.ReplyOutcome.Sent:
                return RedirectWithMessage(id, $"Reply emailed to {result.Request!.Email}.", "success");
            default:
                // Keep what was typed so a failed send does not lose the draft.
                TempData["HelpRequestReplyDraftSubject"] = ReplySubject;
                TempData["HelpRequestReplyDraftBody"] = ReplyBody;
                return RedirectWithMessage(id, result.Error ?? "The reply could not be sent.", "error");
        }
    }

    private IActionResult RedirectWithMessage(Guid id, string message, string kind)
    {
        TempData["HelpRequestMessage"] = message;
        TempData["HelpRequestMessageKind"] = kind;
        return Redirect($"/admin/help/{id}");
    }
}
