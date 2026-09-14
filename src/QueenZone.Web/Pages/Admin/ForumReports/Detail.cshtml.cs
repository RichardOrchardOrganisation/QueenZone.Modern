using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web.Pages.Admin.PrivateMessages;

namespace QueenZone.Web.Pages.Admin.ForumReports;

public sealed class DetailModel(IForumPostReportRepository repository, IMemberAccountRepository members) : AdminPrivateMessageReportsPageModel
{
    public ForumPostReport? Report { get; private set; }
    public MemberAccount? Reporter { get; private set; }
    public MemberAccount? ReportedMember { get; private set; }
    public ForumReportablePost? CurrentPost { get; private set; }
    public string? StatusMessage { get; private set; }
    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Report = await repository.GetAsync(id, cancellationToken);
        if (Report is null) return NotFound();
        Reporter = await members.FindByIdAsync(Report.ReporterMemberId, cancellationToken);
        if (Report.ReportedMemberId is Guid reportedMemberId)
        {
            ReportedMember = await members.FindByIdAsync(reportedMemberId, cancellationToken);
        }
        CurrentPost = await repository.GetVisiblePostAsync(Report.PostId, cancellationToken);
        await repository.AppendViewedAuditAsync(id, EditorEmail, cancellationToken);
        StatusMessage = TempData["ForumReportMessage"] as string;
        ViewData["Title"] = "Reported forum post";
        Breadcrumbs = AdminBreadcrumbs.Page("Reported forum posts", "/admin/forum-reports", "Reported forum post");
        return Page();
    }
}
