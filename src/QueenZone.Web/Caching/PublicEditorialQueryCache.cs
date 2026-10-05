using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class PublicEditorialQueryCache(
    PublicQueryCacheStore store,
    INewsRepository newsRepository,
    IArticlesRepository articlesRepository,
    IArticleRepository communityArticleRepository)
{
    private IMemoryCache cache => store.Cache;
    private IOptions<PublicQueryCacheOptions> options => store.Options;

    public Task<IReadOnlyList<NewsItem>> GetLatestNewsAsync(int count, CancellationToken cancellationToken = default)
    {
        var version = store.GetNewsCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.LatestNews(version, count),
            options.Value.NewsCacheDuration,
            () => newsRepository.GetLatestAsync(count, cancellationToken),
            cancellationToken);
    }

    public Task<int> GetNewsPublishedCountAsync(CancellationToken cancellationToken = default) =>
        GetNewsPublishedCountAsync(NewsArchiveFilter.None, cancellationToken);

    public Task<int> GetNewsPublishedCountAsync(
        NewsArchiveFilter filter,
        CancellationToken cancellationToken = default)
    {
        var version = store.GetNewsCacheVersion();
        var key = filter.IsActive
            ? PublicQueryCacheKeys.NewsPublishedCount(version, filter.DecadeStartYear, filter.Year)
            : PublicQueryCacheKeys.NewsPublishedCount(version);
        return store.GetOrCreateAsync(
            key,
            options.Value.NewsCacheDuration,
            () => newsRepository.GetPublishedCountAsync(filter, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<NewsItem>> GetNewsArchivePageAsync(
        int page,
        int pageSize,
        NewsArchiveFilter filter = default,
        CancellationToken cancellationToken = default)
    {
        var version = store.GetNewsCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.NewsArchivePage(version, page, pageSize, filter.DecadeStartYear, filter.Year),
            options.Value.NewsCacheDuration,
            () => newsRepository.GetArchivePageAsync(page, pageSize, filter, cancellationToken),
            cancellationToken);
    }

    public Task<int> GetArticlePublishedCountAsync(CancellationToken cancellationToken = default)
    {
        var version = store.GetArticleCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.ArticlePublishedCount(version),
            options.Value.ArticleCountCacheDuration,
            () => articlesRepository.GetPublishedCountAsync(cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<ArticleItem>> GetLatestArticlesAsync(int count, CancellationToken cancellationToken = default)
    {
        var version = store.GetArticleCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.LatestArticles(version, count),
            options.Value.ArticleCountCacheDuration,
            () => articlesRepository.GetLatestAsync(count, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<PublishedArticleSubmission>> GetLatestCommunityArticlesAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        var version = store.GetArticleCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.LatestCommunityArticles(version, count),
            options.Value.ArticleCountCacheDuration,
            () => communityArticleRepository.GetPageAsync(1, count, ct: cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<ArticleItem>> GetArticlesArchivePageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var version = store.GetArticleCacheVersion();
        return store.GetOrCreateAsync(
            PublicQueryCacheKeys.ArticlesArchivePage(version, page, pageSize),
            options.Value.ArticleCountCacheDuration,
            () => articlesRepository.GetArchivePageAsync(page, pageSize, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Cached merged {source, id, date} index for <c>/articles</c>. Community
    /// <see cref="SqlException"/> falls back to archive-only and is not cached.
    /// Tag views are community-only.
    /// </summary>
    public async Task<IReadOnlyList<ArticleFeedKey>> GetMergedArticleFeedIndexAsync(
        string? tag = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedTag = string.IsNullOrWhiteSpace(tag) ? null : tag;
        var version = store.GetArticleCacheVersion();
        var key = PublicQueryCacheKeys.ArticleFeedIndex(version, normalizedTag);
        if (cache.TryGetValue(key, out IReadOnlyList<ArticleFeedKey>? cached) && cached is not null)
        {
            return cached;
        }

        var gate = PublicQueryCacheStore.RentLoadGate(key);
        try
        {
            await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (cache.TryGetValue(key, out cached) && cached is not null)
                {
                    return cached;
                }

                var (index, cacheable) = await BuildMergedArticleFeedIndexAsync(normalizedTag, cancellationToken)
                    .ConfigureAwait(false);
                if (cacheable)
                {
                    cache.Set(key, index, options.Value.ArticleCountCacheDuration);
                }

                return index;
            }
            finally
            {
                gate.Semaphore.Release();
            }
        }
        finally
        {
            PublicQueryCacheStore.ReturnLoadGate(key, gate);
        }
    }

    public async Task<IReadOnlyList<ArticleArchiveItem>> HydrateArticleFeedAsync(
        IReadOnlyList<ArticleFeedKey> keys,
        CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
        {
            return [];
        }

        var archiveIds = keys
            .Where(key => key.Source == ArticleFeedSource.Archive)
            .Select(key => key.ArchiveId)
            .ToList();
        var communityIds = keys
            .Where(key => key.Source == ArticleFeedSource.Community)
            .Select(key => key.CommunityId)
            .ToList();

        // Both repositories share the request-scoped QueenZoneDbContext.
        // Load them sequentially so a mixed page cannot start a second EF operation
        // on the same context (#322 / #335).
        IReadOnlyList<ArticleItem> archiveItems = archiveIds.Count == 0
            ? []
            : await articlesRepository.GetPublishedByIdsAsync(archiveIds, cancellationToken)
                .ConfigureAwait(false);
        IReadOnlyList<PublishedArticleSubmission> communityItems = communityIds.Count == 0
            ? []
            : await communityArticleRepository.GetPublishedByIdsAsync(communityIds, cancellationToken)
                .ConfigureAwait(false);

        var archiveMap = archiveItems.ToDictionary(item => item.Id);
        var communityMap = communityItems.ToDictionary(item => item.Id);
        var items = new List<ArticleArchiveItem>(keys.Count);
        foreach (var key in keys)
        {
            if (key.Source == ArticleFeedSource.Archive)
            {
                if (archiveMap.TryGetValue(key.ArchiveId, out var archive))
                {
                    items.Add(PublicContentMapper.ToArticleArchiveItem(archive));
                }

                continue;
            }

            if (communityMap.TryGetValue(key.CommunityId, out var community))
            {
                items.Add(PublicContentMapper.ToCommunityArticleArchiveItem(community));
            }
        }

        return PublicContentMapper.DedupeArticleArchiveItemsByDetailPath(items);
    }

    private async Task<(IReadOnlyList<ArticleFeedKey> Index, bool Cacheable)> BuildMergedArticleFeedIndexAsync(
        string? tag,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ArticleFeedKey> communityKeys;
        var cacheable = true;
        try
        {
            communityKeys = await communityArticleRepository
                .GetPublishedFeedKeysAsync(tag, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SqlException)
        {
            communityKeys = [];
            cacheable = false;
        }

        IReadOnlyList<ArticleFeedKey> archiveKeys = tag is null
            ? await articlesRepository.GetPublishedFeedKeysAsync(cancellationToken).ConfigureAwait(false)
            : [];

        return (ArticleFeedOrdering.Sort(communityKeys.Concat(archiveKeys)), cacheable);
    }

    /// <summary>
    /// Invalidates all public news cache entries (latest lists, archive pages, and published counts)
    /// by bumping the news cache version. Call after publish, unpublish, delete of published news,
    /// or edit of published news.
    /// </summary>
    public void InvalidateNewsCache()
    {
        // Versioned keys mean callers can introduce new latest-count variants without updating
        // invalidation. Previous version entries expire via their normal TTL.
        cache.Set(PublicQueryCacheKeys.NewsVersion, PublicQueryCacheStore.CreateCacheVersion(), PublicQueryCacheStore.VersionEntryOptions);
    }

    /// <summary>
    /// Invalidates public article cache entries (latest lists, archive pages, published count)
    /// by bumping the article cache version.
    /// </summary>
    public void InvalidateArticleCountCache() => InvalidateArticlesCache();

    public void InvalidateArticlesCache()
    {
        cache.Set(PublicQueryCacheKeys.ArticleVersion, PublicQueryCacheStore.CreateCacheVersion(), PublicQueryCacheStore.VersionEntryOptions);
    }

}
