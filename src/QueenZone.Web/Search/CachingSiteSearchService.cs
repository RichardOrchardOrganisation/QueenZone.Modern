using Microsoft.Extensions.Caching.Memory;
using QueenZone.Data;

namespace QueenZone.Web.Search;

/// <summary>
/// Bounded in-process cache around <see cref="ISiteSearchService"/>. Anonymous requests only;
/// never caches short queries, pages beyond <see cref="SiteSearchLimits.MaxPage"/>, exceptions,
/// timeouts, cancellations, or unavailable results. Successful empty pages for real queries
/// are cached. Concurrent misses for the same key share one inner call.
/// </summary>
public sealed class CachingSiteSearchService(
    ISiteSearchService inner,
    SiteSearchResultCache cache,
    IHttpContextAccessor httpContextAccessor,
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

        var key = SiteSearchCacheKey.Normalize(query, contentType, page, pageSize);

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

        var lazy = cache.Inflight.GetOrAdd(
            key,
            static (cacheKey, owner) => new Lazy<Task<SiteSearchPage>>(
                () => owner.FetchAndCacheAsync(cacheKey)),
            this);

        try
        {
            return await lazy.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompleted)
            {
                cache.Inflight.TryRemove(KeyValuePair.Create(key, lazy));
            }
        }
    }

    private async Task<SiteSearchPage> FetchAndCacheAsync(SiteSearchCacheKey key)
    {
        if (TryGetFresh(key, out var hit))
        {
            return hit;
        }

        var result = await inner.SearchAsync(
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
