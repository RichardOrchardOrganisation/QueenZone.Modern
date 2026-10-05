using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class PublicForumQueryCache(
    PublicQueryCacheStore store,
    IForumRepository forumRepository,
    ILiveActivityQueryService liveActivityQuery)
{
    private IMemoryCache cache => store.Cache;
    private IOptions<PublicQueryCacheOptions> options => store.Options;

    public Task<IReadOnlyList<ForumCategoryItem>> GetForumCategoriesAsync(CancellationToken cancellationToken = default) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.ForumCategories,
            options.Value.ForumStatsCacheDuration,
            () => forumRepository.GetCategoriesAsync(cancellationToken),
            cancellationToken);

    public Task<int> GetForumThreadCountAsync(CancellationToken cancellationToken = default) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.ForumThreadCount,
            options.Value.ForumStatsCacheDuration,
            () => forumRepository.GetTotalThreadCountAsync(cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<ForumRecentThreadItem>> GetForumRecentThreadsAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.ForumRecentThreads(count),
            options.Value.ForumStatsCacheDuration,
            () => forumRepository.GetRecentThreadsAsync(count, cancellationToken),
            cancellationToken);

    /// <summary>
    /// John S Stuart's rare/discography posts for the "Rare Discography" page. Long-lived cache:
    /// this legacy-flagged set only changes via re-import, not day-to-day forum activity.
    /// </summary>
    public Task<IReadOnlyList<ForumRecentThreadItem>> GetForumLegacyDiscographyThreadsAsync(
        CancellationToken cancellationToken = default) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.ForumLegacyDiscographyThreads,
            options.Value.ForumStatsCacheDuration,
            () => forumRepository.GetLegacyDiscographyThreadsAsync(cancellationToken),
            cancellationToken);

    /// <summary>
    /// Count of forum posts made today. Short 45s TTL: no presence-tracking exists, so this
    /// is the only honest "live" signal for the mobile home screen's activity strip.
    /// </summary>
    public Task<int> GetLiveActivityNewForumRepliesTodayAsync(CancellationToken cancellationToken = default) =>
        store.GetOrCreateAsync(
            PublicQueryCacheKeys.LiveActivityNewForumReplies,
            options.Value.LiveActivityCacheDuration,
            () => liveActivityQuery.GetNewForumRepliesTodayAsync(cancellationToken),
            cancellationToken);

    public void InvalidateForumStatsCache()
    {
        cache.Remove(PublicQueryCacheKeys.ForumCategories);
        cache.Remove(PublicQueryCacheKeys.ForumThreadCount);
        cache.Remove(PublicQueryCacheKeys.ForumRecentThreads(ForumRoutes.RecentThreadsCount));
        cache.Remove(PublicQueryCacheKeys.ForumLegacyDiscographyThreads);
    }

}
