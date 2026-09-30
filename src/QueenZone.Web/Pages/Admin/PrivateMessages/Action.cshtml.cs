using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.PrivateMessages;

public sealed class ActionModel(IPrivateMessageModerationRepository privateMessageRepository) : AdminPrivateMessageReportsPageModel
{
    [BindProperty]
    public string? Status { get; set; }

    public async Task<IActionResult> OnPostStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Status) || !PrivateMessageReportStatus.IsKnown(Status))
        {
            return RedirectWithMessage(id, "A valid status is required.", "error");
        }

        var updated = await privateMessageRepository.UpdateReportStatusAsync(
            id,
            Status,
            EditorEmail,
            cancellationToken);
        if (updated is null)
        {
            return NotFound();
        }

        TempData["PrivateMessageReportMessage"] = $"Marked as {updated.Status}.";
        TempData["PrivateMessageReportMessageKind"] = "success";
        return Redirect($"/admin/private-messages/{id}");
    }

    private IActionResult RedirectWithMessage(Guid id, string message, string kind)
    {
        TempData["PrivateMessageReportMessage"] = message;
        TempData["PrivateMessageReportMessageKind"] = kind;
        return Redirect($"/admin/private-messages/{id}");
    }
}
