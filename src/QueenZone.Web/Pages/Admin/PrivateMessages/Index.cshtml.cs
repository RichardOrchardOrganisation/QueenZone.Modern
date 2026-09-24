using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.PrivateMessages;

public sealed class IndexModel(IPrivateMessageModerationRepository privateMessageRepository) : AdminPrivateMessageReportsPageModel
{
    public PrivateMessageReportListPage List { get; private set; } =
        new([], 0, PrivateMessageReportStatus.Open);

    public int PageNumber { get; private set; } = 1;

    public string StatusFilter { get; private set; } = PrivateMessageReportStatus.Open;

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task OnGetAsync(
        string? status = PrivateMessageReportStatus.Open,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        PageNumber = Math.Max(1, pageNumber);
        StatusFilter = string.IsNullOrWhiteSpace(status) ? PrivateMessageReportStatus.Open : status;
        List = await privateMessageRepository.ListReportsAsync(StatusFilter, PageNumber, 50, cancellationToken);
        ViewData["Title"] = "Reported messages";
        Breadcrumbs = AdminBreadcrumbs.Section("Reported messages", "/admin/private-messages");
    }
}
