using Microsoft.Extensions.Caching.Memory;
using QueenZone.Data;

namespace QueenZone.Web.Search;

/// <summary>
/// Bounded in-process cache around <see cref="ISiteSearchService"/>. Anonymous requests only;
/// never caches short queries, pages beyond <see cref="SiteSearchLimits.MaxPage"/>, exceptions,
/// timeouts, cancellations, or unavailable results. Successful empty pages for real queries
/// are cached. Concurrent misses for the same key share one inner call that runs in its own
/// DI scope so it can outlive the request that started it.
/// </summary>
public sealed class CachingSiteSearchService(
    ISiteSearchService inner,
    SiteSearchResultCache cache,
    IHttpContextAccessor httpContextAccessor,
    IServiceScopeFactory scopeFactory,
    Func<IServiceProvider, ISiteSearchService> createInner,
    TimeProvider? timeProvider = null) : ISiteSearchService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Test seam: skip the outer cache lookup so <see cref="FetchAndCacheAsync"/> must
    /// recheck before SQL.
    /// </summary>
    internal bool BypassOuterCacheLookup { get; set; }

    public async Task<SiteSearchPage> SearchAsync(
        string query,
        string? contentType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var key = SiteSearchCacheKey.Normalize(query, contentType, page, pageSize) with { Revision = cache.Revision.Value };

        if (SiteSearchLimits.IsBelowMinimumLength(key.Query)
            || SiteSearchLimits.IsBeyondMaxPage(key.Page)
            || IsAuthenticated())
        {
            return await inner.SearchAsync(
                key.Query,
                key.ContentType,
                key.Page,
                key.PageSize,
                cancellationToken).ConfigureAwait(false);
        }

        if (!BypassOuterCacheLookup && TryGetFresh(key, out var hit))
        {
            return hit;
        }

        Lazy<Task<SiteSearchPage>> created = null!;
        created = new Lazy<Task<SiteSearchPage>>(() => StartSharedFetch(key, created));
        var lazy = cache.Inflight.GetOrAdd(key, created);

        return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task<SiteSearchPage> StartSharedFetch(
        SiteSearchCacheKey key,
        Lazy<Task<SiteSearchPage>> lazy)
    {
        var fetch = FetchAndCacheAsync(key);
        // Always drop the inflight entry when the shared task finishes — RanToCompletion,
        // Faulted, or Canceled. Waiters must not be the ones that TryRemove.
        _ = fetch.ContinueWith(
            static (task, state) =>
            {
                var (ownerCache, cacheKey, ownerLazy) =
                    ((SiteSearchResultCache Cache, SiteSearchCacheKey Key, Lazy<Task<SiteSearchPage>> Lazy))state!;
                ownerCache.Inflight.TryRemove(KeyValuePair.Create(cacheKey, ownerLazy));
                _ = task.Exception;
            },
            (cache, key, lazy),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return fetch;
    }

    private async Task<SiteSearchPage> FetchAndCacheAsync(SiteSearchCacheKey key)
    {
        if (TryGetFresh(key, out var hit))
        {
            return hit;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var search = createInner(scope.ServiceProvider);
        if (search is CachingSiteSearchService)
        {
            throw new InvalidOperationException(
                "Shared search fetch must resolve the unwrapped inner service, not ISiteSearchService.");
        }

        var result = await search.SearchAsync(
            key.Query,
            key.ContentType,
            key.Page,
            key.PageSize,
            CancellationToken.None).ConfigureAwait(false);

        cache.Memory.Set(
            key,
            new CachedSearchPage(result, clock.GetUtcNow() + SiteSearchResultCache.AbsoluteExpiration),
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = SiteSearchResultCache.AbsoluteExpiration,
                Size = SiteSearchResultCache.EntrySize,
            });

        return result;
    }

    private bool TryGetFresh(SiteSearchCacheKey key, out SiteSearchPage page)
    {
        if (cache.Memory.TryGetValue(key, out CachedSearchPage? cached)
            && cached is not null
            && cached.ExpiresAt > clock.GetUtcNow())
        {
            page = cached.Page;
            return true;
        }

        page = null!;
        return false;
    }

    private bool IsAuthenticated() =>
        httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    private sealed record CachedSearchPage(SiteSearchPage Page, DateTimeOffset ExpiresAt);
}
