using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using QueenZone.Data;

namespace QueenZone.Web.Search;

/// <summary>
/// Bounded in-process cache around <see cref="ISiteSearchService"/>. Anonymous requests only;
/// never caches short queries, exceptions, timeouts, or cancellations. Concurrent misses for
/// the same key share one inner call.
/// </summary>
public sealed class CachingSiteSearchService(
    ISiteSearchService inner,
    SiteSearchResultCache cache,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider? timeProvider = null) : ISiteSearchService
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<SiteSearchPage>>> Inflight =
        new(StringComparer.Ordinal);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public Task<SiteSearchPage> SearchAsync(
        string query,
        string? contentType,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (SiteSearchLimits.IsBelowMinimumLength(query) || IsAuthenticated())
        {
            return inner.SearchAsync(query, contentType, page, pageSize, cancellationToken);
        }

        var key = BuildKey(query, contentType, page, pageSize);
        if (TryGetFresh(key, out var hit))
        {
            return Task.FromResult(hit);
        }

        var lazy = Inflight.GetOrAdd(
            key,
            static (cacheKey, state) => new Lazy<Task<SiteSearchPage>>(() =>
                state.Owner.FetchAndCacheAsync(
                    cacheKey,
                    state.Query,
                    state.ContentType,
                    state.Page,
                    state.PageSize,
                    state.CancellationToken)),
            (Owner: this, Query: query, ContentType: contentType, Page: page, PageSize: pageSize, CancellationToken: cancellationToken));

        return AwaitInflightAsync(key, lazy);
    }

    private async Task<SiteSearchPage> AwaitInflightAsync(string key, Lazy<Task<SiteSearchPage>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            Inflight.TryRemove(KeyValuePair.Create(key, lazy));
        }
    }

    private async Task<SiteSearchPage> FetchAndCacheAsync(
        string key,
        string query,
        string? contentType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await inner.SearchAsync(query, contentType, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

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

    private bool TryGetFresh(string key, out SiteSearchPage page)
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

    internal static string BuildKey(string query, string? contentType, int page, int pageSize)
    {
        var normalizedQuery = query.Trim().ToLowerInvariant();
        var normalizedType = string.IsNullOrWhiteSpace(contentType)
            ? string.Empty
            : contentType.Trim().ToLowerInvariant();
        return $"search:{normalizedQuery}|{normalizedType}|{page}|{pageSize}|anon";
    }

    private sealed record CachedSearchPage(SiteSearchPage Page, DateTimeOffset ExpiresAt);
}
