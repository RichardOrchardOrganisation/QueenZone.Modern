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

    public SiteSearchResultCache() : this(new SearchIndexRevision())
    {
    }

    public SiteSearchResultCache(SearchIndexRevision revision)
    {
        Revision = revision;
        Memory = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = SizeLimit,
        });
    }

    internal SearchIndexRevision Revision { get; }

    public IMemoryCache Memory { get; }

    internal ConcurrentDictionary<SiteSearchCacheKey, InflightSearch> Inflight { get; } = new();

    public void Dispose() => Memory.Dispose();

    internal sealed class InflightSearch(Func<InflightSearch, Task<SiteSearchPage>> fetch)
    {
        private readonly object gate = new();
        private readonly CancellationTokenSource cancellation = new();
        private int waiters;
        private bool completed;
        private bool abandoned;
        private Lazy<Task<SiteSearchPage>>? task;

        public CancellationToken Token => cancellation.Token;

        public Lazy<Task<SiteSearchPage>> Fetch
        {
            get
            {
                lock (gate)
                {
                    return task ??= new(() => fetch(this));
                }
            }
        }

        public bool TryJoin()
        {
            lock (gate)
            {
                if (abandoned)
                {
                    return false;
                }

                waiters++;
                return true;
            }
        }

        public void Leave()
        {
            lock (gate)
            {
                if (--waiters == 0 && !completed && !(task?.IsValueCreated == true && task.Value.IsCompleted))
                {
                    abandoned = true;
                    cancellation.Cancel();
                }
            }
        }

        public void Complete()
        {
            lock (gate)
            {
                completed = true;
                cancellation.Dispose();
            }
        }
    }
}
