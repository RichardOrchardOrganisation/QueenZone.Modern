namespace QueenZone.Web;

/// <summary>
/// Periodically purges pending audio for Rejected and Withdrawn fan-performance
/// submissions after the 30-day grace period.
/// </summary>
public sealed class FanPerformanceSubmissionPurgeHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<FanPerformanceSubmissionPurgeHostedService> logger)
    : PeriodicScopedHostedService(scopeFactory, timeProvider, logger, DefaultRunInterval)
{
    internal static readonly TimeSpan DefaultRunInterval = TimeSpan.FromHours(24);

    protected override string FailureMessage => "Fan-performance submission purge failed.";

    protected override Task RunJobAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        MaintenanceJobs.RunFanPerformanceSubmissionPurgeAsync(scopedServices, Logger, cancellationToken);
}
