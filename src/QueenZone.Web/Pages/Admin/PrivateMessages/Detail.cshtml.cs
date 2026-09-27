using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Pages.Admin.PrivateMessages;

public sealed class DetailModel(
    IPrivateMessageModerationRepository privateMessageRepository,
    IMemberAccountRepository memberAccountRepository) : AdminPrivateMessageReportsPageModel
{
    public PrivateMessageReport? Report { get; private set; }

    public MemberAccount? Reporter { get; private set; }

    public MemberAccount? Reported { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Report = await privateMessageRepository.GetReportAsync(id, cancellationToken);
        if (Report is null)
        {
            return NotFound();
        }

        Reporter = await memberAccountRepository.FindByIdAsync(Report.ReporterMemberId, cancellationToken);
        Reported = await memberAccountRepository.FindByIdAsync(Report.ReportedMemberId, cancellationToken);

        // Records this admin's access to the report's snapshotted message content (ADR 0015).
        // One row per page load, not per list-view render.
        await privateMessageRepository.AppendReportViewedAuditAsync(id, EditorEmail, cancellationToken);

        StatusMessage = TempData["PrivateMessageReportMessage"] as string;
        StatusMessageKind = TempData["PrivateMessageReportMessageKind"] as string;
        ViewData["Title"] = "Reported message";
        Breadcrumbs = AdminBreadcrumbs.Page("Reported messages", "/admin/private-messages", "Reported message");
        return Page();
    }
}
