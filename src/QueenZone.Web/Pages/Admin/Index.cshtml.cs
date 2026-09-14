using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin;

public sealed class IndexModel(AdminDashboardService dashboardService) : PageModel
{
    public MemberStats MemberStats { get; private set; } = null!;
    public IReadOnlyList<RecentLogin> RecentLogins { get; private set; } = [];
    public IReadOnlyList<DailyRegistration> DailyRegistrations { get; private set; } = [];
    public SubmissionQueueStats SubmissionQueue { get; private set; } = SubmissionQueueStats.Empty;
    public int OpenHelpRequestCount { get; private set; }
    public int OpenPrivateMessageReportCount { get; private set; }
    public int OpenForumPostReportCount { get; private set; }
    public int OpenFanPerformanceReportCount { get; private set; }
    public GoogleAnalyticsTrafficSnapshot Traffic { get; private set; } =
        GoogleAnalyticsTrafficSnapshot.Unavailable("Google Analytics traffic has not loaded.");

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        ViewData["ShowAdminNav"] = true;
        base.OnPageHandlerExecuting(context);
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await dashboardService.GetSnapshotAsync(cancellationToken);
        MemberStats = snapshot.MemberStats;
        RecentLogins = snapshot.RecentLogins;
        DailyRegistrations = snapshot.DailyRegistrations;
        SubmissionQueue = snapshot.SubmissionQueue;
        OpenHelpRequestCount = snapshot.OpenHelpRequestCount;
        OpenPrivateMessageReportCount = snapshot.OpenPrivateMessageReportCount;
        OpenForumPostReportCount = snapshot.OpenForumPostReportCount;
        OpenFanPerformanceReportCount = snapshot.OpenFanPerformanceReportCount;
        Traffic = snapshot.Traffic;
        return Page();
    }
}
