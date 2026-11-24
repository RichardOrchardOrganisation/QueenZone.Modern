using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Application;

public static class MaintenanceWorkerServiceCollectionExtensions
{
    // The worker leaves Apple revocations queued for the web host, which owns the keys.
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

        services.AddQueenZoneApplication();
        services.AddOptions<GalleryOrphanSweepOptions>()
            .Bind(configuration.GetSection(GalleryOrphanSweepOptions.SectionName));
        services.AddSingleton<IValidateOptions<GalleryOrphanSweepOptions>, GalleryOrphanSweepOptionsValidator>();

        services.Replace(ServiceDescriptor.Scoped(provider => new MemberAccountDeletionService(
            provider.GetRequiredService<IMemberAccountRepository>(),
            provider.GetRequiredService<IBlobUploadService>(),
            provider.GetRequiredService<TimeProvider>(),
            appleTokens: null,
            provider.GetRequiredService<ILogger<MemberAccountDeletionService>>(),
            emailSender: null)));

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
