using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web;

public sealed class PublicQueryCacheService(
    IMemoryCache cache,
    IOptions<PublicQueryCacheOptions> options,
    INewsRepository newsRepository,
    IArticlesRepository articlesRepository,
    IForumRepository forumRepository,
    IQueenHistoryRepository queenHistoryRepository,
    IPhotoRepository photoRepository,
    ILiveActivityQueryService liveActivityQuery,
    IFanPerformanceRepository fanPerformanceRepository,
    IQuoteRepository quoteRepository,
    ITriviaRepository triviaRepository,
    IBiographyRepository biographyRepository,
    IDiscographyRepository discographyRepository,
    IFreddieTributeRepository freddieTributeRepository)
{
    private static readonly MemoryCacheEntryOptions VersionEntryOptions = new()
    {
        Priority = CacheItemPriority.NeverRemove
    };

    /// <summary>
    /// Process-wide per-key gates so concurrent cold-cache hits share a single factory execution
    /// even when <see cref="PublicQueryCacheService"/> is scoped (one instance per HTTP request).
    /// Key set is small (news/article version variants, catalog pools, forum stats, history, photo pages).
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LoadGates =
        new(StringComparer.Ordinal);

    public Task<IReadOnlyList<NewsItem>> GetLatestNewsAsync(int count, CancellationToken cancellationToken = default)
    {
        var version = GetNewsCacheVersion();
        return GetOrCreateAsync(
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
        var version = GetNewsCacheVersion();
        var key = filter.IsActive
            ? PublicQueryCacheKeys.NewsPublishedCount(version, filter.DecadeStartYear, filter.Year)
            : PublicQueryCacheKeys.NewsPublishedCount(version);
        return GetOrCreateAsync(
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
        var version = GetNewsCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.NewsArchivePage(version, page, pageSize, filter.DecadeStartYear, filter.Year),
            options.Value.NewsCacheDuration,
            () => newsRepository.GetArchivePageAsync(page, pageSize, filter, cancellationToken),
            cancellationToken);
    }

    public Task<int> GetArticlePublishedCountAsync(CancellationToken cancellationToken = default)
    {
        var version = GetArticleCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.ArticlePublishedCount(version),
            options.Value.ArticleCountCacheDuration,
            () => articlesRepository.GetPublishedCountAsync(cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<ArticleItem>> GetLatestArticlesAsync(int count, CancellationToken cancellationToken = default)
    {
        var version = GetArticleCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.LatestArticles(version, count),
            options.Value.ArticleCountCacheDuration,
            () => articlesRepository.GetLatestAsync(count, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<ArticleItem>> GetArticlesArchivePageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var version = GetArticleCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.ArticlesArchivePage(version, page, pageSize),
            options.Value.ArticleCountCacheDuration,
            () => articlesRepository.GetArchivePageAsync(page, pageSize, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<ForumCategoryItem>> GetForumCategoriesAsync(CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            PublicQueryCacheKeys.ForumCategories,
            options.Value.ForumStatsCacheDuration,
            () => forumRepository.GetCategoriesAsync(cancellationToken),
            cancellationToken);

    public Task<int> GetForumThreadCountAsync(CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            PublicQueryCacheKeys.ForumThreadCount,
            options.Value.ForumStatsCacheDuration,
            () => forumRepository.GetTotalThreadCountAsync(cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<ForumRecentThreadItem>> GetForumRecentThreadsAsync(
        int count,
        CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
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
        GetOrCreateAsync(
            PublicQueryCacheKeys.ForumLegacyDiscographyThreads,
            options.Value.ForumStatsCacheDuration,
            () => forumRepository.GetLegacyDiscographyThreadsAsync(cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<QueenHistoryEvent>> GetOnThisDayAsync(
        DateOnly date,
        int count,
        CancellationToken cancellationToken = default)
    {
        var version = GetHistoryCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.OnThisDay(version, date, count),
            options.Value.OnThisDayCacheDuration,
            () => queenHistoryRepository.GetOnThisDayAsync(date, count, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<QueenHistoryEvent>> GetAroundThisDayAsync(
        DateOnly date,
        int dayWindow,
        int count,
        CancellationToken cancellationToken = default)
    {
        var version = GetHistoryCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.AroundThisDay(version, date, dayWindow, count),
            options.Value.OnThisDayCacheDuration,
            () => queenHistoryRepository.GetAroundThisDayAsync(date, dayWindow, count, cancellationToken),
            cancellationToken);
    }

    public Task<IReadOnlyList<QueenHistoryEvent>> GetAllPublishedHistoryEventsAsync(
        CancellationToken cancellationToken = default)
    {
        var version = GetHistoryCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.AllPublishedHistory(version),
            options.Value.OnThisDayCacheDuration,
            () => queenHistoryRepository.GetAllPublishedAsync(cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Caches the published quote pool and picks <see cref="Random.Shared"/> per request
    /// so consecutive callers do not freeze on one quote.
    /// </summary>
    public async Task<QuoteItem?> GetRandomPublishedQuoteAsync(CancellationToken cancellationToken = default)
    {
        var published = await GetPublishedQuotesAsync(cancellationToken);
        if (published.Count == 0)
        {
            return null;
        }

        return published[Random.Shared.Next(published.Count)];
    }

    /// <summary>
    /// Caches the published trivia pool and picks <see cref="Random.Shared"/> per request
    /// so consecutive callers do not freeze on one fact.
    /// </summary>
    public async Task<TriviaFactItem?> GetRandomPublishedTriviaAsync(CancellationToken cancellationToken = default)
    {
        var published = await GetPublishedTriviaAsync(cancellationToken);
        if (published.Count == 0)
        {
            return null;
        }

        return published[Random.Shared.Next(published.Count)];
    }

    public Task<IReadOnlyList<BiographyChapterItem>> GetBiographyChaptersAsync(
        CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            PublicQueryCacheKeys.BiographyChapters,
            options.Value.CatalogCacheDuration,
            () => biographyRepository.GetChaptersAsync(cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<AlbumSummary>> GetDiscographyAlbumsAsync(
        CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            PublicQueryCacheKeys.DiscographyAlbums,
            options.Value.CatalogCacheDuration,
            () => discographyRepository.GetAlbumsAsync(cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<PhotoCategory>> GetPhotoCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var version = GetPhotoCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.PhotoCategories(version),
            options.Value.PhotoCacheDuration,
            () => photoRepository.GetCategoriesAsync(cancellationToken),
            cancellationToken);
    }

    public async Task<PhotoCategory?> GetPhotoCategoryBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var categories = await GetPhotoCategoriesAsync(cancellationToken);
        return categories.FirstOrDefault(category =>
            string.Equals(category.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Caches one visible tribute id for a few minutes. A miss seeks an indexed id range;
    /// a hit loads that id. Neither path sorts <c>FREDDIE_T</c> with <c>NEWID()</c>.
    /// </summary>
    public async Task<FreddieTribute?> GetFeaturedFreddieTributeAsync(CancellationToken cancellationToken = default)
    {
        var pick = await GetOrCreateAsync(
            PublicQueryCacheKeys.FreddieFeaturedTributeId,
            options.Value.FreddieSampleCacheDuration,
            async () =>
            {
                var id = await freddieTributeRepository.PickRandomVisibleIdAsync(cancellationToken);
                return new CachedId(id);
            },
            cancellationToken);
        if (pick.Id is not int idValue)
        {
            return null;
        }

        return await freddieTributeRepository.GetVisibleByIdAsync(idValue, cancellationToken);
    }

    /// <summary>
    /// Up to four Freddie-category photo ids, cached for a few minutes, then loaded by id.
    /// Category lookup reuses <see cref="GetPhotoCategoriesAsync"/>.
    /// </summary>
    public async Task<IReadOnlyList<PhotoItem>> GetFreddieTributePhotosAsync(
        CancellationToken cancellationToken = default)
    {
        var categories = await GetPhotoCategoriesAsync(cancellationToken);
        var category = categories.FirstOrDefault(item =>
            item.Slug.Contains("freddie", StringComparison.OrdinalIgnoreCase));
        if (category is null)
        {
            return [];
        }

        var version = GetPhotoCacheVersion();
        var ids = await GetOrCreateAsync(
            PublicQueryCacheKeys.FreddiePhotoSample(version, category.CatId),
            options.Value.FreddieSampleCacheDuration,
            () => photoRepository.PickRandomPublishedPhotoIdsAsync(category.CatId, 4, cancellationToken),
            cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        return await photoRepository.GetPublishedByIdsAsync(category.CatId, ids, cancellationToken);
    }

    public Task<PhotoCategoryPage> GetPhotoCategoryPageAsync(
        int catId,
        int page,
        int pageSize,
        PhotoListFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var activeFilter = filter ?? PhotoListFilter.None;
        var version = GetPhotoCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.PhotoCategoryPage(version, catId, page, pageSize, activeFilter.QueryValue),
            options.Value.PhotoCacheDuration,
            () => photoRepository.GetCategoryPageAsync(catId, page, pageSize, activeFilter, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Count of forum posts made today. Short 45s TTL: no presence-tracking exists, so this
    /// is the only honest "live" signal for the mobile home screen's activity strip.
    /// </summary>
    public Task<int> GetLiveActivityNewForumRepliesTodayAsync(CancellationToken cancellationToken = default) =>
        GetOrCreateAsync(
            PublicQueryCacheKeys.LiveActivityNewForumReplies,
            options.Value.LiveActivityCacheDuration,
            () => liveActivityQuery.GetNewForumRepliesTodayAsync(cancellationToken),
            cancellationToken);

    public Task<IReadOnlyList<FanPerformance>> GetFanPerformancePageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var version = GetFanPerformanceCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.FanPerformancePage(version, page, pageSize),
            options.Value.FanPerformanceCacheDuration,
            () => fanPerformanceRepository.GetPageAsync(page, pageSize, cancellationToken),
            cancellationToken);
    }

    public Task<int> GetFanPerformanceVisibleCountAsync(CancellationToken cancellationToken = default)
    {
        var version = GetFanPerformanceCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.FanPerformanceVisibleCount(version),
            options.Value.FanPerformanceCacheDuration,
            () => fanPerformanceRepository.GetVisibleCountAsync(cancellationToken),
            cancellationToken);
    }

    public Task<FanPerformance?> GetFanPerformanceByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var version = GetFanPerformanceCacheVersion();
        return GetOrCreateAsync(
            PublicQueryCacheKeys.FanPerformanceById(version, id),
            options.Value.FanPerformanceCacheDuration,
            () => fanPerformanceRepository.GetByIdAsync(id, cancellationToken),
            cancellationToken);
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
        cache.Set(PublicQueryCacheKeys.NewsVersion, CreateCacheVersion(), VersionEntryOptions);
    }

    public void InvalidateForumStatsCache()
    {
        cache.Remove(PublicQueryCacheKeys.ForumCategories);
        cache.Remove(PublicQueryCacheKeys.ForumThreadCount);
        cache.Remove(PublicQueryCacheKeys.ForumRecentThreads(ForumRoutes.RecentThreadsCount));
        cache.Remove(PublicQueryCacheKeys.ForumLegacyDiscographyThreads);
    }

    /// <summary>
    /// Invalidates public article cache entries (latest lists, archive pages, published count)
    /// by bumping the article cache version.
    /// </summary>
    public void InvalidateArticleCountCache() => InvalidateArticlesCache();

    public void InvalidateArticlesCache()
    {
        cache.Set(PublicQueryCacheKeys.ArticleVersion, CreateCacheVersion(), VersionEntryOptions);
    }

    public void InvalidateQuotesCache() => cache.Remove(PublicQueryCacheKeys.PublishedQuotes);

    public void InvalidateTriviaCache() => cache.Remove(PublicQueryCacheKeys.PublishedTrivia);

    public void InvalidateBiographyCache() => cache.Remove(PublicQueryCacheKeys.BiographyChapters);

    /// <summary>
    /// Evicts the public discography album list. No admin write path exists today;
    /// TTL is the freshness fallback until a sync/admin writer is wired.
    /// </summary>
    public void InvalidateDiscographyCache() => cache.Remove(PublicQueryCacheKeys.DiscographyAlbums);

    /// <summary>
    /// Bumps the photo cache version so category lists and paged grids refresh after admin writes.
    /// </summary>
    public void InvalidatePhotoCache()
    {
        cache.Set(PublicQueryCacheKeys.PhotoVersion, CreateCacheVersion(), VersionEntryOptions);
    }

    public void InvalidateHistoryCache()
    {
        cache.Set(PublicQueryCacheKeys.HistoryVersion, CreateCacheVersion(), VersionEntryOptions);
    }

    /// <summary>
    /// Bumps the fan-performance cache version so archive pages and the
    /// <c>/api/v1</c> content projection refresh after admin writes.
    /// </summary>
    public void InvalidateFanPerformanceCache()
    {
        cache.Set(PublicQueryCacheKeys.FanPerformanceVersion, CreateCacheVersion(), VersionEntryOptions);
    }

    private string GetNewsCacheVersion() => GetOrInitVersion(PublicQueryCacheKeys.NewsVersion);

    private string GetArticleCacheVersion() => GetOrInitVersion(PublicQueryCacheKeys.ArticleVersion);

    private string GetPhotoCacheVersion() => GetOrInitVersion(PublicQueryCacheKeys.PhotoVersion);

    private string GetHistoryCacheVersion() => GetOrInitVersion(PublicQueryCacheKeys.HistoryVersion);

    private string GetFanPerformanceCacheVersion() => GetOrInitVersion(PublicQueryCacheKeys.FanPerformanceVersion);

    private string GetOrInitVersion(string key)
    {
        if (cache.TryGetValue(key, out string? version) && !string.IsNullOrEmpty(version))
        {
            return version;
        }

        var initial = "0";
        cache.Set(key, initial, VersionEntryOptions);
        return initial;
    }

    private Task<IReadOnlyList<QuoteItem>> GetPublishedQuotesAsync(CancellationToken cancellationToken) =>
        GetOrCreateAsync(
            PublicQueryCacheKeys.PublishedQuotes,
            options.Value.CatalogCacheDuration,
            async () =>
            {
                var all = await quoteRepository.GetAllAsync(cancellationToken);
                IReadOnlyList<QuoteItem> published = all.Where(quote => quote.IsPublished).ToList();
                return published;
            },
            cancellationToken);

    private Task<IReadOnlyList<TriviaFactItem>> GetPublishedTriviaAsync(CancellationToken cancellationToken) =>
        GetOrCreateAsync(
            PublicQueryCacheKeys.PublishedTrivia,
            options.Value.CatalogCacheDuration,
            async () =>
            {
                var all = await triviaRepository.GetAllAsync(cancellationToken);
                IReadOnlyList<TriviaFactItem> published = all.Where(fact => fact.IsPublished).ToList();
                return published;
            },
            cancellationToken);

    private static string CreateCacheVersion() => Guid.NewGuid().ToString("N");

    private sealed record CachedId(int? Id);

    private async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan duration,
        Func<Task<T>> factory,
        CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var gate = LoadGates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
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
            gate.Release();
        }
    }
}
