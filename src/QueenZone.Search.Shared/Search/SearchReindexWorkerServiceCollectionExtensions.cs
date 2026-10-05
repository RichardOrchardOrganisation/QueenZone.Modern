using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace QueenZone.Search.Shared;

/// <summary>
/// DI registration for the standalone <c>QueenZone.SearchReindex.Worker</c> console host, which
/// (unlike the public web app) has no <c>AddQueenZoneData</c> composition root to lean on, so
/// this registers <see cref="SearchReindexBuilder"/> itself alongside the scheduled-worker
/// pieces. Callers still need <c>AddQueenZoneLegacyData</c>/<c>AddQueenZoneInMemoryData</c> from
/// <c>QueenZone.Data</c> for the repositories <see cref="SearchReindexBuilder"/> depends on.
/// </summary>
public static class SearchReindexWorkerServiceCollectionExtensions
{
    public static IServiceCollection AddQueenZoneSearchReindexWorker(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        if (configuration is not null)
        {
            services.AddOptions<SearchReindexSchedulerOptions>()
                .Bind(configuration.GetSection(SearchReindexSchedulerOptions.SectionName));
        }
        else
        {
            services.AddOptions<SearchReindexSchedulerOptions>();
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CatalogSearchReindexer>();
        services.AddScoped<SearchReindexBuilder>();
        services.AddScoped<SearchReindexScheduledWorker>();
        return services;
    }
}
