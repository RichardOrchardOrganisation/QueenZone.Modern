using System.Diagnostics;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Retention and cleanup jobs. Production runs them from <c>QueenZone.Maintenance.Worker</c> on
/// the operator machine; the web host only runs them when
/// <see cref="RunInWebHostConfigurationKey"/> is true (Development) so a sweep never competes
/// with page requests or deploy health probes (#1677, #666).
/// </summary>
public static class MaintenanceJobs
{
    public const string RunInWebHostConfigurationKey = "MaintenanceJobs:RunInWebHost";

    public const string MemberAccountDeletion = "member-account-deletion";
    public const string PrivateMessageReportPurge = "private-message-report-purge";
    public const string FanPerformanceSubmissionPurge = "fan-performance-submission-purge";
    public const string GalleryOrphanSweep = "gallery-orphan-sweep";

    public const string AllSelector = "all";
    public const string SixHourlySelector = "six-hourly";
    public const string DailySelector = "daily";

    /// <summary>Jobs that ran about every 6 hours as web hosted services.</summary>
    public static IReadOnlyList<string> SixHourly { get; } = [MemberAccountDeletion, GalleryOrphanSweep];

    /// <summary>Jobs that ran about every 24 hours as web hosted services.</summary>
    public static IReadOnlyList<string> Daily { get; } = [PrivateMessageReportPurge, FanPerformanceSubmissionPurge];

    public static IReadOnlyList<string> All { get; } = [.. SixHourly, .. Daily];

    /// <summary>Expands a schedule selector or single job name; null when unknown.</summary>
    public static IReadOnlyList<string>? Resolve(string selector) => selector switch
    {
        AllSelector => All,
        SixHourlySelector => SixHourly,
        DailySelector => Daily,
        _ when All.Contains(selector, StringComparer.Ordinal) => [selector],
        _ => null,
    };

    public static Task RunAsync(
        string job,
        IServiceProvider scopedServices,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken) => job switch
        {
            MemberAccountDeletion => RunMemberAccountDeletionAsync(scopedServices, timeProvider, logger, cancellationToken),
            PrivateMessageReportPurge => RunPrivateMessageReportPurgeAsync(scopedServices, timeProvider, logger, cancellationToken),
            FanPerformanceSubmissionPurge => RunFanPerformanceSubmissionPurgeAsync(scopedServices, logger, cancellationToken),
            GalleryOrphanSweep => RunGalleryOrphanSweepAsync(scopedServices, logger, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(job), job, "Unknown maintenance job."),
        };

    internal static async Task RunMemberAccountDeletionAsync(
        IServiceProvider scopedServices,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var activity = QueenZoneTelemetry.ActivitySource.StartActivity(
            "MemberAccountDeletion",
            ActivityKind.Internal);
        var service = scopedServices.GetRequiredService<MemberAccountService>();
        var purged = await service.PurgeDueDeletionsAsync(
            timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        if (purged > 0)
        {
            logger.LogInformation(
                "Purged personal data for {PurgedAccountCount} deleted member account(s).",
                purged);
        }
    }

    internal static async Task RunPrivateMessageReportPurgeAsync(
        IServiceProvider scopedServices,
        TimeProvider timeProvider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var activity = QueenZoneTelemetry.ActivitySource.StartActivity(
            "PrivateMessageReportPurge",
            ActivityKind.Internal);
        var repository = scopedServices.GetRequiredService<IPrivateMessageRepository>();
        var purged = await repository.PurgeExpiredReportsAsync(
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (purged > 0)
        {
            logger.LogInformation(
                "Purged {PurgedReportCount} private-message report(s) past the retention window.",
                purged);
        }
    }

    internal static async Task RunFanPerformanceSubmissionPurgeAsync(
        IServiceProvider scopedServices,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using var activity = QueenZoneTelemetry.ActivitySource.StartActivity(
            "FanPerformanceSubmissionPurge",
            ActivityKind.Internal);
        var service = scopedServices.GetRequiredService<FanPerformanceSubmissionPurgeService>();
        var result = await service.PurgeAsync(cancellationToken);
        if (result.Deleted > 0 || result.Failures > 0)
        {
            logger.LogInformation(
                "Purged {Deleted} pending fan-performance blob(s) ({Failures} failure(s), {Candidates} candidate(s)).",
                result.Deleted,
                result.Failures,
                result.Candidates);
        }
    }

    internal static async Task RunGalleryOrphanSweepAsync(
        IServiceProvider scopedServices,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!scopedServices.GetRequiredService<IOptions<GalleryOrphanSweepOptions>>().Value.Enabled)
        {
            return;
        }

        using var activity = QueenZoneTelemetry.ActivitySource.StartActivity(
            "GalleryOrphanSweep",
            ActivityKind.Internal);
        var service = scopedServices.GetRequiredService<GalleryOrphanSweepService>();
        var result = await service.SweepAsync(cancellationToken);
        if (result.OrphansFound > 0)
        {
            logger.LogInformation(
                "Gallery orphan sweep scanned {BlobsScanned} blob(s), found {OrphansFound} orphan(s), " +
                "deleted {OrphansDeleted}, {DeleteFailures} delete failure(s).",
                result.BlobsScanned,
                result.OrphansFound,
                result.OrphansDeleted,
                result.DeleteFailures);
        }
    }
}
