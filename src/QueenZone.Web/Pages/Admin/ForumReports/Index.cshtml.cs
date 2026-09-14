using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Web.Pages.Admin.PrivateMessages;

namespace QueenZone.Web.Pages.Admin.ForumReports;

public sealed class IndexModel(IForumPostReportRepository repository) : AdminPrivateMessageReportsPageModel
{
    public ForumPostReportListPage List { get; private set; } = new([], 0, PrivateMessageReportStatus.Open);
    public string StatusFilter { get; private set; } = PrivateMessageReportStatus.Open;
    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string? status = PrivateMessageReportStatus.Open, int page = 1, CancellationToken cancellationToken = default)
    {
        StatusFilter = string.IsNullOrWhiteSpace(status) ? PrivateMessageReportStatus.Open : status;
        if (!StatusFilter.Equals("all", StringComparison.OrdinalIgnoreCase) && !PrivateMessageReportStatus.IsKnown(StatusFilter))
        {
            return BadRequest("Unknown report status.");
        }
        List = await repository.ListAsync(StatusFilter, Math.Max(1, page), 50, cancellationToken);
        ViewData["Title"] = "Reported forum posts";
        Breadcrumbs = AdminBreadcrumbs.Page("Admin", "/admin", "Reported forum posts");
        return Page();
    }
}
