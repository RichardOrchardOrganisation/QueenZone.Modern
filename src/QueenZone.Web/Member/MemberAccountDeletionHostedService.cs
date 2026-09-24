namespace QueenZone.Web;

public sealed class MemberAccountDeletionHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<MemberAccountDeletionHostedService> logger) : BackgroundService
{
    /// <summary>
    /// Wait until after the App Service container start probe window
    /// (<c>WEBSITES_CONTAINER_START_TIME_LIMIT</c> defaults to 230s) before the
    /// first purge. A cold Blob Storage delete on this timer previously ran
    /// in the same window as <c>/health</c> probes (#666).
    /// </summary>
    internal static readonly TimeSpan DefaultStartupDelay = TimeSpan.FromMinutes(5);

    internal static readonly TimeSpan DefaultRunInterval = TimeSpan.FromHours(6);

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
                await MaintenanceJobs.RunMemberAccountDeletionAsync(scope.ServiceProvider, timeProvider, logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Member account deletion purge failed.");
            }

            await Task.Delay(RunInterval, timeProvider, stoppingToken);
        }
    }
}
