using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using QueenZone.Data;

namespace QueenZone.NewsAgent;

public sealed class NewsAgentGuidanceProvider(
    INewsAgentGuidanceRepository repository,
    IMemoryCache cache,
    ILogger<NewsAgentGuidanceProvider> logger) : INewsAgentGuidanceProvider
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<NewsAgentGuidanceSnapshot> GetPublishedAsync(
        NewsAgentGuidanceType type,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKey(type);
        if (cache.TryGetValue(cacheKey, out NewsAgentGuidanceSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var published = await repository.GetPublishedAsync(type, cancellationToken);
            if (published is null)
            {
                logger.LogInformation(
                    "News agent guidance fallback for {GuidanceType}: {FallbackReason}.",
                    NewsAgentGuidanceText.ToStorageType(type),
                    "missing");
                return CacheSnapshot(cacheKey, NewsAgentGuidanceSnapshot.Empty);
            }

            var sanitized = NewsAgentGuidanceText.Sanitize(published.Content);

            var snapshot = new NewsAgentGuidanceSnapshot(
                published.Id,
                published.RevisionNumber,
                published.ContentHash,
                sanitized);
            return CacheSnapshot(cacheKey, snapshot);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "News agent guidance fallback for {GuidanceType}: {FallbackReason}.",
                NewsAgentGuidanceText.ToStorageType(type),
                "unavailable");
            return NewsAgentGuidanceSnapshot.Empty;
        }
    }

    public void Invalidate(NewsAgentGuidanceType type) => cache.Remove(CacheKey(type));

    private NewsAgentGuidanceSnapshot CacheSnapshot(string cacheKey, NewsAgentGuidanceSnapshot snapshot)
    {
        cache.Set(
            cacheKey,
            snapshot,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            });
        return snapshot;
    }

    internal static string CacheKey(NewsAgentGuidanceType type) =>
        "news-agent-guidance:" + NewsAgentGuidanceText.ToStorageType(type);
}
