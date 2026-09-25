using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class GalleryOrphanSweepHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<GalleryOrphanSweepOptions> options,
    TimeProvider timeProvider,
    ILogger<GalleryOrphanSweepHostedService> logger)
    : PeriodicScopedHostedService(scopeFactory, timeProvider, logger, DefaultRunInterval)
{
    internal static readonly TimeSpan DefaultRunInterval = TimeSpan.FromHours(6);

    protected override string FailureMessage => "Gallery orphan sweep failed.";

    protected override bool IsEnabled => options.Value.Enabled;

    protected override Task RunJobAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        MaintenanceJobs.RunGalleryOrphanSweepAsync(scopedServices, Logger, cancellationToken);
}
