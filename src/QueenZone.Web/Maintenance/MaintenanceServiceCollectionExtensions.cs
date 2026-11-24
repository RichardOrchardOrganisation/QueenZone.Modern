using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

public static class MaintenanceServiceCollectionExtensions
{
    /// <summary>
    /// Web host side of #1677. The retention and sweep hosted services run only when
    /// <see cref="MaintenanceJobs.RunInWebHostConfigurationKey"/> is true (Development), so local
    /// runs still purge. Otherwise the operator machine runs them from
    /// <c>QueenZone.Maintenance.Worker</c>, and the web host keeps only Apple token revocation,
    /// which needs its data-protection key ring.
    /// </summary>
    public static IServiceCollection AddQueenZoneMaintenanceHostedServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (configuration.GetValue<bool>(MaintenanceJobs.RunInWebHostConfigurationKey))
        {
            services.AddHostedService<MemberAccountDeletionHostedService>();
            services.AddHostedService<PrivateMessageReportPurgeHostedService>();
            services.AddHostedService<FanPerformanceSubmissionPurgeHostedService>();
            services.AddHostedService<GalleryOrphanSweepHostedService>();
            return services;
        }

        var authentication = configuration
            .GetSection(MemberAuthenticationOptions.SectionName)
            .Get<MemberAuthenticationOptions>();
        if (authentication?.Apple?.IsConfigured == true)
        {
            services.AddHostedService<AppleTokenRevocationHostedService>();
        }

        return services;
    }

}
