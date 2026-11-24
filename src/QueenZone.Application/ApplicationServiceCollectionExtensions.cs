using Microsoft.Extensions.DependencyInjection.Extensions;

namespace QueenZone.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddQueenZoneApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<MemberAccountDeletionService>();
        services.AddScoped<FanPerformanceSubmissionPurgeService>();
        services.AddScoped<GalleryOrphanSweepService>();
        services.AddSingleton<MaintenanceJobRunner>();
        return services;
    }
}
