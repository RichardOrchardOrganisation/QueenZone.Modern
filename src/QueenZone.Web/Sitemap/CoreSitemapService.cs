using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace QueenZone.Web.Sitemap;

public sealed class CoreSitemapService(
    CoreSitemapBuilder builder,
    IMemoryCache cache,
    IOptions<SitemapOptions> options,
    IOutputCacheStore outputCacheStore) : ICoreSitemapService
{
    private const string CacheKeyPrefix = "sitemap-core.xml";
    private const string VersionKey = "sitemap-core.version";

    private static readonly MemoryCacheEntryOptions VersionEntryOptions = new()
    {
        Priority = CacheItemPriority.NeverRemove
    };

    public async Task<string> GetXmlAsync(string publicBaseUrl, CancellationToken cancellationToken = default)
    {
        var version = GetCacheVersion();
        var cacheKey = $"{CacheKeyPrefix}:{version}:{publicBaseUrl.TrimEnd('/')}";
        return await cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(options.Value.CacheHours);
            var sitemapEntries = await builder.BuildAsync(cancellationToken);
            return SitemapXmlWriter.WriteUrlSet(sitemapEntries, publicBaseUrl);
        }) ?? string.Empty;
    }

    /// <summary>
    /// Drops the in-memory core sitemap XML and the shared public sitemap output-cache tag.
    /// Call after admin news publish / unpublish / delete / edit of published articles so
    /// crawlers see updated detail URLs without waiting for the full TTL.
    /// </summary>
    public async Task InvalidateAsync(CancellationToken cancellationToken = default)
    {
        cache.Set(VersionKey, CreateCacheVersion(), VersionEntryOptions);
        await outputCacheStore.EvictByTagAsync(PublicOutputCachePolicies.PublicSitemapTag, cancellationToken);
    }

    private string GetCacheVersion()
    {
        if (cache.TryGetValue(VersionKey, out string? version) && !string.IsNullOrEmpty(version))
        {
            return version;
        }

        var initial = "0";
        cache.Set(VersionKey, initial, VersionEntryOptions);
        return initial;
    }

    private static string CreateCacheVersion() => Guid.NewGuid().ToString("N");
}
