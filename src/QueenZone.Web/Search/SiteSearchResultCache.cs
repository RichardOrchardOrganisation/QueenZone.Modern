using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using QueenZone.Data;

namespace QueenZone.Web.Search;

/// <summary>
/// Dedicated <see cref="IMemoryCache"/> for anonymous site-search pages. Separate from the
/// app-wide memory cache so search entries cannot evict public-query or auth entries, and
/// so the size limit applies only to this corpus.
/// </summary>
public sealed class SiteSearchResultCache : IDisposable
{
    public const long SizeLimit = 256;

    public const int EntrySize = 1;

    public static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromSeconds(60);

    public SiteSearchResultCache()
    {
        Memory = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = SizeLimit,
        });
    }

    public IMemoryCache Memory { get; }

    internal ConcurrentDictionary<SiteSearchCacheKey, Lazy<Task<SiteSearchPage>>> Inflight { get; } = new();

    public void Dispose() => Memory.Dispose();
}
