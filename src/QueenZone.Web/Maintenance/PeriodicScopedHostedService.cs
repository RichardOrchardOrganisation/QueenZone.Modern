namespace QueenZone.Web;

/// <summary>
/// Runs one job on a fixed interval, each run in its own DI scope: wait
/// <see cref="StartupDelay"/>, then loop { run the job, log and swallow any failure that is not
/// host shutdown, wait <see cref="RunInterval"/> }.
/// </summary>
public abstract class PeriodicScopedHostedService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger logger,
    TimeSpan defaultRunInterval) : BackgroundService
{
    /// <summary>
    /// Wait until after the App Service container start probe window
    /// (<c>WEBSITES_CONTAINER_START_TIME_LIMIT</c> defaults to 230s) before the
    /// first run. A cold Blob Storage delete on this timer previously ran
    /// in the same window as <c>/health</c> probes (#666).
    /// </summary>
    internal static readonly TimeSpan DefaultStartupDelay = TimeSpan.FromMinutes(5);

    internal TimeSpan StartupDelay { get; init; } = DefaultStartupDelay;

    internal TimeSpan RunInterval { get; init; } = defaultRunInterval;

    protected ILogger Logger => logger;

    /// <summary>Message logged (with the exception) when a run throws.</summary>
    protected abstract string FailureMessage { get; }

    /// <summary>When false the run is skipped but the loop keeps its schedule.</summary>
    protected virtual bool IsEnabled => true;

    protected abstract Task RunJobAsync(IServiceProvider scopedServices, CancellationToken cancellationToken);

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
            if (IsEnabled)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await RunJobAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "{FailureMessage}", FailureMessage);
                }
            }

            await Task.Delay(RunInterval, timeProvider, stoppingToken);
        }
    }
}
