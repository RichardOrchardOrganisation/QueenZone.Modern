using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class PublicQueryCacheStore(
    IMemoryCache cache,
    IOptions<PublicQueryCacheOptions> options)
{
    internal IMemoryCache Cache => cache;
    internal IOptions<PublicQueryCacheOptions> Options => options;

    internal static readonly MemoryCacheEntryOptions VersionEntryOptions = new()
    {
        Priority = CacheItemPriority.NeverRemove
    };

    /// <summary>
    /// Process-wide per-key gates so concurrent cold-cache hits share a single factory execution
    /// even when <see cref="PublicQueryCacheService"/> is scoped (one instance per HTTP request).
    /// Keys include unbounded page/date/id/version variants. Retain a gate only while its holder
    /// or registered waiters use it; cardinality is the number of currently active distinct keys,
    /// and returns to zero when loads finish. Completed keys and semaphores are not retained.
    /// </summary>
    private static readonly Dictionary<string, LoadGate> LoadGates = new(StringComparer.Ordinal);
    private static readonly object LoadGatesSync = new();

    internal string GetNewsCacheVersion() => GetOrInitVersion(PublicQueryCacheKeys.NewsVersion);

    internal string GetArticleCacheVersion() => GetOrInitVersion(PublicQueryCacheKeys.ArticleVersion);

    internal string GetPhotoCacheVersion() => GetOrInitVersion(PublicQueryCacheKeys.PhotoVersion);

    internal string GetHistoryCacheVersion() => GetOrInitVersion(PublicQueryCacheKeys.HistoryVersion);

    internal string GetFanPerformanceCacheVersion() => GetOrInitVersion(PublicQueryCacheKeys.FanPerformanceVersion);

    internal string GetOrInitVersion(string key)
    {
        if (cache.TryGetValue(key, out string? version) && !string.IsNullOrEmpty(version))
        {
            return version;
        }

        var initial = "0";
        cache.Set(key, initial, VersionEntryOptions);
        return initial;
    }

    internal static string CreateCacheVersion() => Guid.NewGuid().ToString("N");

    internal async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan duration,
        Func<Task<T>> factory,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var gate = RentLoadGate(key);
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (cache.TryGetValue(key, out cached) && cached is not null)
                {
                    return cached;
                }

                var value = await factory().ConfigureAwait(false);
                cache.Set(key, value, duration);
                return value;
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }
        finally
        {
            ReturnLoadGate(key, gate);
        }
    }

    internal static LoadGate RentLoadGate(string key)
    {
        lock (LoadGatesSync)
        {
            if (!LoadGates.TryGetValue(key, out var gate))
            {
                gate = new LoadGate();
                LoadGates.Add(key, gate);
            }

            // Register before waiting so a releasing holder cannot remove a waiter's gate.
            gate.ReferenceCount++;
            return gate;
        }
    }

    internal static void ReturnLoadGate(string key, LoadGate gate)
    {
        lock (LoadGatesSync)
        {
            // Includes callers cancelled before acquiring the semaphore. Removal and rent
            // share this lock, so the final reference cannot race with a new caller.
            if (--gate.ReferenceCount == 0)
            {
                LoadGates.Remove(key);
                gate.Semaphore.Dispose();
            }
        }
    }

    internal sealed class LoadGate
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount { get; set; }
    }

}
