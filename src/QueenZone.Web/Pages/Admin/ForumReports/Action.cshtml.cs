using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Web.Pages.Admin.PrivateMessages;

namespace QueenZone.Web.Pages.Admin.ForumReports;

public sealed class ActionModel(IForumPostReportRepository repository) : AdminPrivateMessageReportsPageModel
{
    [BindProperty] public string? Status { get; set; }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!PrivateMessageReportStatus.IsKnown(Status)) return BadRequest("A valid status is required.");
        var report = await repository.UpdateStatusAsync(id, Status!, EditorEmail, cancellationToken);
        if (report is null) return NotFound();
        TempData["ForumReportMessage"] = $"Marked as {report.Status}.";
        return Redirect($"/admin/forum-reports/{id}");
    }
}
