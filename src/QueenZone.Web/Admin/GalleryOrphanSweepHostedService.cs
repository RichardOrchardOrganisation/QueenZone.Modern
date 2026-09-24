using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class GalleryOrphanSweepHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<GalleryOrphanSweepOptions> options,
    TimeProvider timeProvider,
    ILogger<GalleryOrphanSweepHostedService> logger) : BackgroundService
{
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
            if (!options.Value.Enabled)
            {
                await Task.Delay(RunInterval, timeProvider, stoppingToken);
                continue;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await MaintenanceJobs.RunGalleryOrphanSweepAsync(scope.ServiceProvider, logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Gallery orphan sweep failed.");
            }

            await Task.Delay(RunInterval, timeProvider, stoppingToken);
        }
    }
}
