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

    /// <summary>
    /// Composition for <c>QueenZone.Maintenance.Worker</c>: only what the four jobs need.
    /// <see cref="AppleAccountTokenService"/> is deliberately absent. The worker cannot decrypt
    /// the web host's protected tokens, so <see cref="MemberAccountService"/> leaves revocations
    /// queued for <see cref="AppleTokenRevocationHostedService"/>.
    /// </summary>
    public static IServiceCollection AddQueenZoneMaintenanceWorker(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var sqlConnectionString = configuration.GetConnectionString("QueenZoneLegacy");
        var blobConnectionString = configuration.GetConnectionString(BlobUploadOptions.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(sqlConnectionString) != string.IsNullOrWhiteSpace(blobConnectionString))
        {
            // Real SQL with in-memory blobs would mark account-deletion blobs complete without
            // deleting them; real blobs with sample SQL would sweep live photos as orphans.
            throw new InvalidOperationException(
                $"Set both ConnectionStrings:QueenZoneLegacy and ConnectionStrings:{BlobUploadOptions.ConnectionStringName}, or neither for sample data.");
        }

        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddOptions<UploadQuotaOptions>()
            .Bind(configuration.GetSection(UploadQuotaOptions.SectionName));
        services.AddOptions<GalleryOrphanSweepOptions>()
            .Bind(configuration.GetSection(GalleryOrphanSweepOptions.SectionName));
        services.AddSingleton<IValidateOptions<GalleryOrphanSweepOptions>, GalleryOrphanSweepOptionsValidator>();

        services.AddSingleton<MemberUploadQuotaService>();
        services.AddScoped<MemberAccountService>();
        services.AddScoped<FanPerformanceSubmissionPurgeService>();
        services.AddScoped<GalleryOrphanSweepService>();
        services.AddSingleton<MaintenanceJobRunner>();

        if (string.IsNullOrWhiteSpace(sqlConnectionString))
        {
            services.AddQueenZoneInMemoryData();
        }
        else
        {
            var forumDataOptions = configuration
                .GetSection(ForumDataOptions.SectionName)
                .Get<ForumDataOptions>() ?? new ForumDataOptions();
            services.AddQueenZoneLegacyData(sqlConnectionString, forumDataOptions);
        }

        services.AddQueenZoneStorage(configuration);
        return services;
    }
}
