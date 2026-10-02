using Microsoft.AspNetCore.Mvc.Rendering;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.FanPerformanceReports;

public sealed class IndexModel(IFanPerformanceReportRepository reportRepository)
    : AdminFanPerformanceReportsPageModel
{
    public FanPerformanceReportListPage List { get; private set; } =
        new([], 0, FanPerformanceReportStatus.Open);

    public int PageNumber { get; private set; } = 1;

    public string StatusFilter { get; private set; } = FanPerformanceReportStatus.Open;

    public IReadOnlyList<SelectListItem> StatusOptions { get; private set; } = [];

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task OnGetAsync(
        string? status = FanPerformanceReportStatus.Open,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        PageNumber = Math.Max(1, pageNumber);
        StatusFilter = NormalizeFilter(status);
        StatusOptions = new[] { "all" }.Concat(FanPerformanceReportStatus.All)
            .Select(value => new SelectListItem(value == "all" ? "All" : value, value, value == StatusFilter))
            .ToArray();
        List = await reportRepository.ListAsync(
            StatusFilter,
            PageNumber,
            FanPerformanceReportLimits.ListPageSize,
            cancellationToken);
        ViewData["Title"] = "Fan performance reports";
        Breadcrumbs = AdminBreadcrumbs.Section("Fan performance reports", "/admin/fan-performance-reports");
    }
    private static string NormalizeFilter(string? status)
    {
        if (string.Equals(status?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return "all";
        }

        return FanPerformanceReportStatus.IsKnown(status)
            ? FanPerformanceReportStatus.Normalize(status!)
            : FanPerformanceReportStatus.Open;
    }
}
