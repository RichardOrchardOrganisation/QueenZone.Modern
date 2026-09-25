using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Permanently deletes private-message reports that reached a terminal status (Dismissed or
/// Actioned) more than <see cref="PrivateMessageLimits.ReportRetentionAfterTerminalStatus"/> ago
/// (ADR 0015 decision 2). Open and Reviewed reports are never purged by this service.
/// </summary>
public sealed class PrivateMessageReportPurgeHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PrivateMessageReportPurgeHostedService> logger)
    : PeriodicScopedHostedService(scopeFactory, timeProvider, logger, DefaultRunInterval)
{
    /// <summary>
    /// The retention window is 180 days; a daily sweep is frequent enough that no report is
    /// retained meaningfully longer than the documented policy.
    /// </summary>
    internal static readonly TimeSpan DefaultRunInterval = TimeSpan.FromHours(24);

    private readonly TimeProvider _timeProvider = timeProvider;

    protected override string FailureMessage => "Private-message report purge failed.";

    protected override Task RunJobAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        MaintenanceJobs.RunPrivateMessageReportPurgeAsync(scopedServices, _timeProvider, Logger, cancellationToken);
}
