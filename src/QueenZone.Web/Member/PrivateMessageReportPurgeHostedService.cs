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
    ILogger<PrivateMessageReportPurgeHostedService> logger) : BackgroundService
{
    /// <summary>
    /// Same startup-delay rationale as <see cref="MemberAccountDeletionHostedService"/>: avoid
    /// running background purge work inside the App Service container start probe window.
    /// </summary>
    internal static readonly TimeSpan DefaultStartupDelay = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The retention window is 180 days; a daily sweep is frequent enough that no report is
    /// retained meaningfully longer than the documented policy.
    /// </summary>
    internal static readonly TimeSpan DefaultRunInterval = TimeSpan.FromHours(24);

    internal TimeSpan StartupDelay { get; init; } = DefaultStartupDelay;

    internal TimeSpan RunInterval { get; init; } = DefaultRunInterval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, timeProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await MaintenanceJobs.RunPrivateMessageReportPurgeAsync(scope.ServiceProvider, timeProvider, logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Private-message report purge failed.");
            }

            await Task.Delay(RunInterval, timeProvider, stoppingToken);
        }
    }
}
