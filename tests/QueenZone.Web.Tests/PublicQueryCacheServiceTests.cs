using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class PublicQueryCacheServiceTests
{
    [Fact]
    public async Task LatestNewsAndPublishedCountAreCachedUntilInvalidated()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new CountingNewsRepository();
        var service = CreateService(memoryCache, newsRepository: newsRepository);

        var firstLatest = await service.GetLatestNewsAsync(5);
        var secondLatest = await service.GetLatestNewsAsync(5);
        var firstPublishedCount = await service.GetNewsPublishedCountAsync();
        var secondPublishedCount = await service.GetNewsPublishedCountAsync();

        Assert.Same(firstLatest, secondLatest);
        Assert.Equal(firstLatest[0].Title, secondLatest[0].Title);
        Assert.Equal(firstPublishedCount, secondPublishedCount);
        Assert.Equal(1, newsRepository.LatestCallCount);
        Assert.Equal(1, newsRepository.PublishedCountCallCount);

        service.InvalidateNewsCache();
        var thirdLatest = await service.GetLatestNewsAsync(5);
        var thirdPublishedCount = await service.GetNewsPublishedCountAsync();

        Assert.NotSame(firstLatest, thirdLatest);
        Assert.NotEqual(firstLatest[0].Title, thirdLatest[0].Title);
        Assert.NotEqual(firstPublishedCount, thirdPublishedCount);
        Assert.Equal(2, newsRepository.LatestCallCount);
        Assert.Equal(2, newsRepository.PublishedCountCallCount);
    }

    [Fact]
    public async Task ArchiveCountsAreCachedPerContentType()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new CountingNewsRepository();
        var articlesRepository = new CountingArticlesRepository();
        var service = CreateService(
            memoryCache,
            newsRepository: newsRepository,
            articlesRepository: articlesRepository);

        await service.GetNewsPublishedCountAsync();
        await service.GetNewsPublishedCountAsync();
        await service.GetArticlePublishedCountAsync();
        await service.GetArticlePublishedCountAsync();

        Assert.Equal(1, newsRepository.PublishedCountCallCount);
        Assert.Equal(1, articlesRepository.PublishedCountCallCount);
    }

    [Fact]
    public async Task ForumCategoriesAndThreadCountAreCached()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var forumRepository = new CountingForumRepository();
        var service = CreateService(memoryCache, forumRepository: forumRepository);

        await service.GetForumCategoriesAsync();
        await service.GetForumCategoriesAsync();
        await service.GetForumThreadCountAsync();
        await service.GetForumThreadCountAsync();
        await service.GetForumRecentThreadsAsync(ForumRoutes.RecentThreadsCount);
        await service.GetForumRecentThreadsAsync(ForumRoutes.RecentThreadsCount);

        Assert.Equal(1, forumRepository.CategoriesCallCount);
        Assert.Equal(1, forumRepository.ThreadCountCallCount);
        Assert.Equal(1, forumRepository.RecentThreadsCallCount);
    }

    [Fact]
    public async Task InvalidateForumStatsCache_evicts_categories_thread_count_and_recent_threads()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var forumRepository = new CountingForumRepository();
        var service = CreateService(memoryCache, forumRepository: forumRepository);

        await service.GetForumCategoriesAsync();
        await service.GetForumThreadCountAsync();
        await service.GetForumRecentThreadsAsync(ForumRoutes.RecentThreadsCount);

        service.InvalidateForumStatsCache();

        await service.GetForumCategoriesAsync();
        await service.GetForumThreadCountAsync();
        await service.GetForumRecentThreadsAsync(ForumRoutes.RecentThreadsCount);

        Assert.Equal(2, forumRepository.CategoriesCallCount);
        Assert.Equal(2, forumRepository.ThreadCountCallCount);
        Assert.Equal(2, forumRepository.RecentThreadsCallCount);
    }

    [Fact]
    public async Task InvalidateNewsCache_does_not_evict_forum_or_history_cache()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new CountingNewsRepository();
        var forumRepository = new CountingForumRepository();
        var historyRepository = new CountingQueenHistoryRepository();
        var service = CreateService(
            memoryCache,
            newsRepository: newsRepository,
            forumRepository: forumRepository,
            historyRepository: historyRepository);

        await service.GetLatestNewsAsync(5);
        await service.GetForumCategoriesAsync();
        await service.GetForumThreadCountAsync();
        await service.GetOnThisDayAsync(new DateOnly(2026, 7, 6), 3);
        await service.GetAroundThisDayAsync(new DateOnly(2026, 7, 6), 7, 3);

        service.InvalidateNewsCache();

        await service.GetLatestNewsAsync(5);
        await service.GetForumCategoriesAsync();
        await service.GetForumThreadCountAsync();
        await service.GetOnThisDayAsync(new DateOnly(2026, 7, 6), 3);
        await service.GetAroundThisDayAsync(new DateOnly(2026, 7, 6), 7, 3);

        Assert.Equal(2, newsRepository.LatestCallCount);
        Assert.Equal(1, forumRepository.CategoriesCallCount);
        Assert.Equal(1, forumRepository.ThreadCountCallCount);
        Assert.Equal(1, historyRepository.OnThisDayCallCount);
        Assert.Equal(1, historyRepository.AroundThisDayCallCount);
    }

    [Fact]
    public async Task InvalidateNewsCache_does_not_evict_article_published_count_cache()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new CountingNewsRepository();
        var articlesRepository = new CountingArticlesRepository();
        var service = CreateService(
            memoryCache,
            newsRepository: newsRepository,
            articlesRepository: articlesRepository);

        await service.GetNewsPublishedCountAsync();
        await service.GetArticlePublishedCountAsync();

        service.InvalidateNewsCache();

        await service.GetNewsPublishedCountAsync();
        await service.GetArticlePublishedCountAsync();

        Assert.Equal(2, newsRepository.PublishedCountCallCount);
        Assert.Equal(1, articlesRepository.PublishedCountCallCount);
    }

    [Fact]
    public async Task InvalidateArticleCountCache_evicts_article_published_count_only()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new CountingNewsRepository();
        var articlesRepository = new CountingArticlesRepository();
        var service = CreateService(
            memoryCache,
            newsRepository: newsRepository,
            articlesRepository: articlesRepository);

        await service.GetNewsPublishedCountAsync();
        await service.GetArticlePublishedCountAsync();
        await service.GetArticlePublishedCountAsync();
        Assert.Equal(1, articlesRepository.PublishedCountCallCount);

        service.InvalidateArticleCountCache();

        await service.GetArticlePublishedCountAsync();
        await service.GetNewsPublishedCountAsync();

        Assert.Equal(2, articlesRepository.PublishedCountCallCount);
        Assert.Equal(1, newsRepository.PublishedCountCallCount);
    }

    [Fact]
    public async Task InvalidateNewsCache_evicts_all_latest_count_variants_not_just_homepage_default()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new CountingNewsRepository();
        var service = CreateService(memoryCache, newsRepository: newsRepository);

        await service.GetLatestNewsAsync(3);
        await service.GetLatestNewsAsync(5);
        await service.GetLatestNewsAsync(10);
        await service.GetNewsPublishedCountAsync();

        Assert.Equal(3, newsRepository.LatestCallCount);
        Assert.Equal(1, newsRepository.PublishedCountCallCount);

        service.InvalidateNewsCache();

        await service.GetLatestNewsAsync(3);
        await service.GetLatestNewsAsync(5);
        await service.GetLatestNewsAsync(10);
        await service.GetNewsPublishedCountAsync();

        Assert.Equal(6, newsRepository.LatestCallCount);
        Assert.Equal(2, newsRepository.PublishedCountCallCount);
    }

    [Fact]
    public async Task LatestNewsCacheKeys_are_isolated_by_count_until_version_bump()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new CountingNewsRepository();
        var service = CreateService(memoryCache, newsRepository: newsRepository);

        var latest3 = await service.GetLatestNewsAsync(3);
        var latest5 = await service.GetLatestNewsAsync(5);
        await service.GetLatestNewsAsync(3);
        await service.GetLatestNewsAsync(5);

        Assert.Equal(2, newsRepository.LatestCallCount);
        Assert.NotSame(latest3, latest5);

        service.InvalidateNewsCache();
        var latest3After = await service.GetLatestNewsAsync(3);

        Assert.Equal(3, newsRepository.LatestCallCount);
        Assert.NotSame(latest3, latest3After);
    }

    [Fact]
    public async Task Concurrent_cold_cache_hits_invoke_factory_once()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new SlowCountingNewsRepository(TimeSpan.FromMilliseconds(100));
        var service = CreateService(memoryCache, newsRepository: newsRepository);

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => service.GetLatestNewsAsync(5))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, newsRepository.LatestCallCount);
        Assert.All(tasks, t => Assert.Same(tasks[0].Result, t.Result));
    }

    [Fact]
    public async Task Concurrent_cold_cache_hits_across_scoped_instances_invoke_factory_once()
    {
        // Production registers PublicQueryCacheService as scoped; gates must be process-wide.
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new SlowCountingNewsRepository(TimeSpan.FromMilliseconds(100));
        var services = Enumerable.Range(0, 12)
            .Select(_ => CreateService(memoryCache, newsRepository: newsRepository))
            .ToArray();

        var tasks = services.Select(s => s.GetLatestNewsAsync(5)).ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, newsRepository.LatestCallCount);
        Assert.All(tasks, t => Assert.Same(tasks[0].Result, t.Result));
    }

    [Fact]
    public async Task Waiting_for_busy_cache_key_observes_cancellation()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new BlockingNewsRepository();
        var service = CreateService(memoryCache, newsRepository: newsRepository);

        var first = service.GetLatestNewsAsync(5);
        await newsRepository.WaitUntilEnteredAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetLatestNewsAsync(5, timeout.Token));

        newsRepository.Release();
        _ = await first;
    }

    [Fact]
    public async Task PublicWarmupService_times_out_slow_cache_prime()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new CancellationBlockingNewsRepository();
        var cache = CreateService(memoryCache, newsRepository: newsRepository);
        await using var provider = CreateWarmupProvider(cache);
        var warmup = new PublicWarmupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<PublicWarmupService>.Instance,
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            warmup.WarmPublicCachesAsync());

        await newsRepository.WaitUntilEnteredAsync();
    }

    [Fact]
    public async Task PublicWarmupService_primes_cache_steps_concurrently()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var gate = new ConcurrentEntryGate(PublicWarmupService.StepCount);
        var cache = CreateService(
            memoryCache,
            newsRepository: new BarrierNewsRepository(gate),
            articlesRepository: new BarrierArticlesRepository(gate),
            forumRepository: new BarrierForumRepository(gate),
            historyRepository: new BarrierQueenHistoryRepository(gate),
            photoRepository: new BarrierPhotoRepository(gate));
        await using var provider = CreateWarmupProvider(cache);
        var warmup = new PublicWarmupService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<PublicWarmupService>.Instance,
            TimeSpan.FromSeconds(2));

        await warmup.WarmPublicCachesAsync();

        Assert.Equal(PublicWarmupService.StepCount, gate.Entered);
    }

    [Fact]
    public async Task OnThisDayCacheVariesByDateAndCount()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var historyRepository = new CountingQueenHistoryRepository();
        var service = CreateService(memoryCache, historyRepository: historyRepository);

        var july6Count3First = await service.GetOnThisDayAsync(new DateOnly(2026, 7, 6), 3);
        var july6Count3Second = await service.GetOnThisDayAsync(new DateOnly(2026, 7, 6), 3);
        var july7Count3 = await service.GetOnThisDayAsync(new DateOnly(2026, 7, 7), 3);
        var july6Count4 = await service.GetOnThisDayAsync(new DateOnly(2026, 7, 6), 4);
        var aroundFirst = await service.GetAroundThisDayAsync(new DateOnly(2026, 7, 6), 7, 3);
        var aroundSecond = await service.GetAroundThisDayAsync(new DateOnly(2026, 7, 6), 7, 3);

        Assert.Same(july6Count3First, july6Count3Second);
        Assert.Same(aroundFirst, aroundSecond);
        Assert.NotSame(july6Count3First, july7Count3);
        Assert.NotSame(july6Count3First, july6Count4);
        Assert.Equal("on-this-day:2026-07-06:3", july6Count3First[0].Title);
        Assert.Equal("on-this-day:2026-07-07:3", july7Count3[0].Title);
        Assert.Equal("on-this-day:2026-07-06:4", july6Count4[0].Title);
        Assert.Equal(3, historyRepository.OnThisDayCallCount);
        Assert.Equal(1, historyRepository.AroundThisDayCallCount);
    }

    [Fact]
    public async Task InvalidateHistoryCache_causes_a_second_on_this_day_repo_hit()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var historyRepository = new CountingQueenHistoryRepository();
        var service = CreateService(memoryCache, historyRepository: historyRepository);

        await service.GetOnThisDayAsync(new DateOnly(2026, 7, 6), 3);
        await service.GetOnThisDayAsync(new DateOnly(2026, 7, 6), 3);
        Assert.Equal(1, historyRepository.OnThisDayCallCount);

        service.InvalidateHistoryCache();

        await service.GetOnThisDayAsync(new DateOnly(2026, 7, 6), 3);

        Assert.Equal(2, historyRepository.OnThisDayCallCount);
    }

    [Fact]
    public async Task PhotoCategoriesAndPagesAreCachedUntilInvalidated()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var photoRepository = new CountingPhotoRepository();
        var service = CreateService(memoryCache, photoRepository: photoRepository);

        var firstCategories = await service.GetPhotoCategoriesAsync();
        var secondCategories = await service.GetPhotoCategoriesAsync();
        var firstPage = await service.GetPhotoCategoryPageAsync(9, 1, 24);
        var secondPage = await service.GetPhotoCategoryPageAsync(9, 1, 24);

        Assert.Same(firstCategories, secondCategories);
        Assert.Same(firstPage, secondPage);
        Assert.Equal(1, photoRepository.CategoriesCallCount);
        Assert.Equal(1, photoRepository.PageCallCount);

        service.InvalidatePhotoCache();

        _ = await service.GetPhotoCategoriesAsync();
        _ = await service.GetPhotoCategoryPageAsync(9, 1, 24);

        Assert.Equal(2, photoRepository.CategoriesCallCount);
        Assert.Equal(2, photoRepository.PageCallCount);
    }

    [Fact]
    public async Task FanPerformancePagesAreCachedUntilInvalidated()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var fanPerformanceRepository = new CountingFanPerformanceRepository();
        var service = CreateService(memoryCache, fanPerformanceRepository: fanPerformanceRepository);

        var firstPage = await service.GetFanPerformancePageAsync(1, 20);
        var secondPage = await service.GetFanPerformancePageAsync(1, 20);
        var firstCount = await service.GetFanPerformanceVisibleCountAsync();
        var secondCount = await service.GetFanPerformanceVisibleCountAsync();
        var firstDetail = await service.GetFanPerformanceByIdAsync(187);
        var secondDetail = await service.GetFanPerformanceByIdAsync(187);

        Assert.Same(firstPage, secondPage);
        Assert.Equal(firstCount, secondCount);
        Assert.Same(firstDetail, secondDetail);
        Assert.Equal(1, fanPerformanceRepository.PageCallCount);
        Assert.Equal(1, fanPerformanceRepository.CountCallCount);
        Assert.Equal(1, fanPerformanceRepository.ByIdCallCount);

        service.InvalidateFanPerformanceCache();

        _ = await service.GetFanPerformancePageAsync(1, 20);
        _ = await service.GetFanPerformanceVisibleCountAsync();
        _ = await service.GetFanPerformanceByIdAsync(187);

        Assert.Equal(2, fanPerformanceRepository.PageCallCount);
        Assert.Equal(2, fanPerformanceRepository.CountCallCount);
        Assert.Equal(2, fanPerformanceRepository.ByIdCallCount);
    }

    [Fact]
    public async Task LiveActivityCountIsCachedForTheConfiguredDuration()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var liveActivityQuery = new CountingLiveActivityQueryService { NextResult = 2 };
        var service = CreateService(memoryCache, liveActivityQuery: liveActivityQuery);

        var first = await service.GetLiveActivityNewForumRepliesTodayAsync();
        liveActivityQuery.NextResult = 5;
        var second = await service.GetLiveActivityNewForumRepliesTodayAsync();

        Assert.Equal(2, first);
        Assert.Equal(2, second);
        Assert.Equal(1, liveActivityQuery.CallCount);
    }

    [Fact]
    public async Task NewsArchivePagesAndFilteredCountsAreCachedUntilInvalidated()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new CountingNewsRepository();
        var service = CreateService(memoryCache, newsRepository: newsRepository);
        var decade = new NewsArchiveFilter(2010);
        var year = new NewsArchiveFilter(null, 2008);

        var firstPage = await service.GetNewsArchivePageAsync(1, 20);
        var secondPage = await service.GetNewsArchivePageAsync(1, 20);
        var otherPage = await service.GetNewsArchivePageAsync(2, 20);
        var decadePage = await service.GetNewsArchivePageAsync(1, 20, decade);
        await service.GetNewsPublishedCountAsync();
        await service.GetNewsPublishedCountAsync();
        await service.GetNewsPublishedCountAsync(decade);
        await service.GetNewsPublishedCountAsync(decade);
        await service.GetNewsPublishedCountAsync(year);
        await service.GetNewsPublishedCountAsync(NewsArchiveFilter.None);

        Assert.Same(firstPage, secondPage);
        Assert.NotSame(firstPage, otherPage);
        Assert.NotSame(firstPage, decadePage);
        Assert.Equal(3, newsRepository.ArchivePageCallCount);
        Assert.Equal(3, newsRepository.PublishedCountCallCount);

        service.InvalidateNewsCache();

        _ = await service.GetNewsArchivePageAsync(1, 20);
        _ = await service.GetNewsArchivePageAsync(2, 20);
        _ = await service.GetNewsArchivePageAsync(1, 20, decade);
        _ = await service.GetNewsPublishedCountAsync();
        _ = await service.GetNewsPublishedCountAsync(decade);
        _ = await service.GetNewsPublishedCountAsync(year);

        Assert.Equal(6, newsRepository.ArchivePageCallCount);
        Assert.Equal(6, newsRepository.PublishedCountCallCount);
    }

    [Fact]
    public async Task ArticleArchiveLatestAndCountShareArticleVersionInvalidation()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var articlesRepository = new CountingArticlesRepository();
        var service = CreateService(memoryCache, articlesRepository: articlesRepository);

        var firstArchive = await service.GetArticlesArchivePageAsync(1, 12);
        var secondArchive = await service.GetArticlesArchivePageAsync(1, 12);
        var otherArchive = await service.GetArticlesArchivePageAsync(2, 12);
        var firstLatest = await service.GetLatestArticlesAsync(3);
        var secondLatest = await service.GetLatestArticlesAsync(3);
        await service.GetArticlePublishedCountAsync();
        await service.GetArticlePublishedCountAsync();

        Assert.Same(firstArchive, secondArchive);
        Assert.NotSame(firstArchive, otherArchive);
        Assert.Same(firstLatest, secondLatest);
        Assert.Equal(2, articlesRepository.ArchivePageCallCount);
        Assert.Equal(1, articlesRepository.LatestCallCount);
        Assert.Equal(1, articlesRepository.PublishedCountCallCount);

        service.InvalidateArticlesCache();

        _ = await service.GetArticlesArchivePageAsync(1, 12);
        _ = await service.GetArticlesArchivePageAsync(2, 12);
        _ = await service.GetLatestArticlesAsync(3);
        _ = await service.GetArticlePublishedCountAsync();

        Assert.Equal(4, articlesRepository.ArchivePageCallCount);
        Assert.Equal(2, articlesRepository.LatestCallCount);
        Assert.Equal(2, articlesRepository.PublishedCountCallCount);
    }

    [Fact]
    public async Task AllPublishedHistoryEventsAreCachedUntilInvalidated()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var historyRepository = new CountingQueenHistoryRepository();
        var service = CreateService(memoryCache, historyRepository: historyRepository);

        var first = await service.GetAllPublishedHistoryEventsAsync();
        var second = await service.GetAllPublishedHistoryEventsAsync();

        Assert.Same(first, second);
        Assert.Equal(1, historyRepository.AllPublishedCallCount);

        service.InvalidateHistoryCache();

        var third = await service.GetAllPublishedHistoryEventsAsync();

        Assert.NotSame(first, third);
        Assert.Equal(2, historyRepository.AllPublishedCallCount);
    }

    [Fact]
    public async Task RandomQuotePicksFromCachedPublishedPool()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var quoteRepository = new CountingQuoteRepository(
            new QuoteItem(1, "Published one", "Freddie", DateTime.UtcNow, true),
            new QuoteItem(2, "Published two", "Brian", DateTime.UtcNow, true),
            new QuoteItem(3, "Draft", "Roger", DateTime.UtcNow, false));
        var service = CreateService(memoryCache, quoteRepository: quoteRepository);

        var first = await service.GetRandomPublishedQuoteAsync();
        var second = await service.GetRandomPublishedQuoteAsync();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first!.IsPublished);
        Assert.True(second!.IsPublished);
        Assert.Contains(first.Id, new[] { 1, 2 });
        Assert.Contains(second.Id, new[] { 1, 2 });
        Assert.Equal(1, quoteRepository.AllCallCount);

        service.InvalidateQuotesCache();
        _ = await service.GetRandomPublishedQuoteAsync();

        Assert.Equal(2, quoteRepository.AllCallCount);
    }

    [Fact]
    public async Task RandomQuoteReturnsNullWhenPublishedPoolIsEmpty()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var quoteRepository = new CountingQuoteRepository(
            new QuoteItem(3, "Draft", "Roger", DateTime.UtcNow, false));
        var service = CreateService(memoryCache, quoteRepository: quoteRepository);

        Assert.Null(await service.GetRandomPublishedQuoteAsync());
        Assert.Null(await service.GetRandomPublishedQuoteAsync());
        Assert.Equal(1, quoteRepository.AllCallCount);
    }

    [Fact]
    public async Task Concurrent_random_quote_cold_hits_load_published_pool_once()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var quoteRepository = new SlowCountingQuoteRepository(
            TimeSpan.FromMilliseconds(100),
            new QuoteItem(1, "Published one", "Freddie", DateTime.UtcNow, true),
            new QuoteItem(2, "Published two", "Brian", DateTime.UtcNow, true));
        var service = CreateService(memoryCache, quoteRepository: quoteRepository);

        var tasks = Enumerable.Range(0, 12)
            .Select(_ => service.GetRandomPublishedQuoteAsync())
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, quoteRepository.AllCallCount);
        Assert.All(tasks, t => Assert.NotNull(t.Result));
    }

    [Fact]
    public async Task RandomTriviaPicksFromCachedPublishedPool()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var triviaRepository = new CountingTriviaRepository(
            new TriviaFactItem(1, "Published one", DateTime.UtcNow, true),
            new TriviaFactItem(2, "Published two", DateTime.UtcNow, true),
            new TriviaFactItem(3, "Draft", DateTime.UtcNow, false));
        var service = CreateService(memoryCache, triviaRepository: triviaRepository);

        var first = await service.GetRandomPublishedTriviaAsync();
        var second = await service.GetRandomPublishedTriviaAsync();

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first!.IsPublished);
        Assert.True(second!.IsPublished);
        Assert.Equal(1, triviaRepository.AllCallCount);

        service.InvalidateTriviaCache();
        _ = await service.GetRandomPublishedTriviaAsync();

        Assert.Equal(2, triviaRepository.AllCallCount);
    }

    [Fact]
    public async Task BiographyChaptersAreCachedUntilInvalidated()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var biographyRepository = new CountingBiographyRepository();
        var service = CreateService(memoryCache, biographyRepository: biographyRepository);

        var first = await service.GetBiographyChaptersAsync();
        var second = await service.GetBiographyChaptersAsync();

        Assert.Same(first, second);
        Assert.Equal(1, biographyRepository.ChaptersCallCount);

        service.InvalidateBiographyCache();

        var third = await service.GetBiographyChaptersAsync();

        Assert.NotSame(first, third);
        Assert.Equal(2, biographyRepository.ChaptersCallCount);
    }

    [Fact]
    public async Task DiscographyAlbumsAreCachedUntilInvalidated()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var discographyRepository = new CountingDiscographyRepository();
        var service = CreateService(memoryCache, discographyRepository: discographyRepository);

        var first = await service.GetDiscographyAlbumsAsync();
        var second = await service.GetDiscographyAlbumsAsync();

        Assert.Same(first, second);
        Assert.Equal(1, discographyRepository.AlbumsCallCount);

        service.InvalidateDiscographyCache();

        var third = await service.GetDiscographyAlbumsAsync();

        Assert.NotSame(first, third);
        Assert.Equal(2, discographyRepository.AlbumsCallCount);
    }

    [Fact]
    public async Task CatalogInvalidation_does_not_evict_unrelated_families()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var newsRepository = new CountingNewsRepository();
        var quoteRepository = new CountingQuoteRepository(
            new QuoteItem(1, "Published", "Freddie", DateTime.UtcNow, true));
        var triviaRepository = new CountingTriviaRepository(
            new TriviaFactItem(1, "Published", DateTime.UtcNow, true));
        var biographyRepository = new CountingBiographyRepository();
        var discographyRepository = new CountingDiscographyRepository();
        var service = CreateService(
            memoryCache,
            newsRepository: newsRepository,
            quoteRepository: quoteRepository,
            triviaRepository: triviaRepository,
            biographyRepository: biographyRepository,
            discographyRepository: discographyRepository);

        await service.GetLatestNewsAsync(5);
        await service.GetRandomPublishedQuoteAsync();
        await service.GetRandomPublishedTriviaAsync();
        await service.GetBiographyChaptersAsync();
        await service.GetDiscographyAlbumsAsync();

        service.InvalidateQuotesCache();

        await service.GetLatestNewsAsync(5);
        await service.GetRandomPublishedQuoteAsync();
        await service.GetRandomPublishedTriviaAsync();
        await service.GetBiographyChaptersAsync();
        await service.GetDiscographyAlbumsAsync();

        Assert.Equal(1, newsRepository.LatestCallCount);
        Assert.Equal(2, quoteRepository.AllCallCount);
        Assert.Equal(1, triviaRepository.AllCallCount);
        Assert.Equal(1, biographyRepository.ChaptersCallCount);
        Assert.Equal(1, discographyRepository.AlbumsCallCount);
    }

    [Fact]
    public async Task FreddieSample_WarmCache_DoesNotPickIdsAgain()
    {
        var tributes = new CountingFreddieTributeRepository(
            new FreddieTribute(7, "Maya", "Still shining.", "India", "24 November 2001", "10:00"));
        var photos = new CountingFreddiePhotoRepository();
        var cache = CreateService(
            new MemoryCache(new MemoryCacheOptions()),
            photoRepository: photos,
            freddieTributeRepository: tributes);

        var first = await cache.GetFeaturedFreddieTributeAsync();
        var second = await cache.GetFeaturedFreddieTributeAsync();
        Assert.Equal(7, first!.Id);
        Assert.Equal(7, second!.Id);
        Assert.Equal(1, tributes.PickCallCount);
        Assert.Equal(2, tributes.ByIdCallCount);

        var firstPhotos = await cache.GetFreddieTributePhotosAsync();
        var secondPhotos = await cache.GetFreddieTributePhotosAsync();
        Assert.Equal(4, firstPhotos.Count);
        Assert.Equal(firstPhotos.Select(item => item.PicId), secondPhotos.Select(item => item.PicId));
        Assert.Equal(1, photos.PickCallCount);
        Assert.Equal(2, photos.ByIdCallCount);
        Assert.Equal(1, photos.CategoriesCallCount);
    }

    private static ServiceProvider CreateWarmupProvider(PublicQueryCacheService cache)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cache);
        return services.BuildServiceProvider();
    }

    private static PublicQueryCacheService CreateService(
        IMemoryCache memoryCache,
        INewsRepository? newsRepository = null,
        IArticlesRepository? articlesRepository = null,
        IForumRepository? forumRepository = null,
        IQueenHistoryRepository? historyRepository = null,
        IPhotoRepository? photoRepository = null,
        ILiveActivityQueryService? liveActivityQuery = null,
        IFanPerformanceRepository? fanPerformanceRepository = null,
        IQuoteRepository? quoteRepository = null,
        ITriviaRepository? triviaRepository = null,
        IBiographyRepository? biographyRepository = null,
        IDiscographyRepository? discographyRepository = null,
        IFreddieTributeRepository? freddieTributeRepository = null,
        PublicQueryCacheOptions? options = null) =>
        new(
            memoryCache,
            Options.Create(options ?? new PublicQueryCacheOptions()),
            newsRepository ?? new CountingNewsRepository(),
            articlesRepository ?? new CountingArticlesRepository(),
            forumRepository ?? new CountingForumRepository(),
            historyRepository ?? new CountingQueenHistoryRepository(),
            photoRepository ?? new CountingPhotoRepository(),
            liveActivityQuery ?? new CountingLiveActivityQueryService(),
            fanPerformanceRepository ?? new CountingFanPerformanceRepository(),
            quoteRepository ?? new CountingQuoteRepository(),
            triviaRepository ?? new CountingTriviaRepository(),
            biographyRepository ?? new CountingBiographyRepository(),
            discographyRepository ?? new CountingDiscographyRepository(),
            freddieTributeRepository ?? new UnusedFreddieTributeRepository());

    private class CountingLiveActivityQueryService : ILiveActivityQueryService
    {
        public int CallCount { get; private set; }

        public int NextResult { get; set; } = 7;

        public Task<int> GetNewForumRepliesTodayAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(NextResult);
        }
    }

    private sealed class SlowCountingNewsRepository(TimeSpan delay) : CountingNewsRepository
    {
        public override async Task<IReadOnlyList<NewsItem>> GetLatestAsync(
            int count,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return await base.GetLatestAsync(count, cancellationToken);
        }
    }

    private sealed class BlockingNewsRepository : CountingNewsRepository
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilEnteredAsync() => entered.Task;

        public void Release() => release.TrySetResult();

        public override async Task<IReadOnlyList<NewsItem>> GetLatestAsync(
            int count,
            CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await release.Task;
            return await base.GetLatestAsync(count, cancellationToken);
        }
    }

    private sealed class CancellationBlockingNewsRepository : CountingNewsRepository
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task WaitUntilEnteredAsync() => entered.Task;

        public override async Task<IReadOnlyList<NewsItem>> GetLatestAsync(
            int count,
            CancellationToken cancellationToken = default)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return await base.GetLatestAsync(count, cancellationToken);
        }
    }

    private class CountingNewsRepository : INewsRepository
    {
        public int LatestCallCount { get; private set; }

        public int PublishedCountCallCount { get; private set; }

        public int ArchivePageCallCount { get; private set; }

        public virtual Task<IReadOnlyList<NewsItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default)
        {
            LatestCallCount++;
            var item = new NewsItem(
                1,
                $"Cached news {LatestCallCount}",
                "Cached news excerpt.",
                "Cached news body.",
                new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
                null,
                true);
            return Task.FromResult<IReadOnlyList<NewsItem>>([item]);
        }

        public virtual Task<int> GetPublishedCountAsync(NewsArchiveFilter filter = default, CancellationToken cancellationToken = default)
        {
            PublishedCountCallCount++;
            return Task.FromResult(PublishedCountCallCount);
        }

        public Task<IReadOnlyList<NewsItem>> GetArchivePageAsync(
            int page,
            int pageSize,
            NewsArchiveFilter filter = default,
            CancellationToken cancellationToken = default)
        {
            ArchivePageCallCount++;
            var item = new NewsItem(
                page,
                $"Cached archive {page}:{pageSize}:{filter.DecadeStartYear}:{filter.Year}",
                "Cached news excerpt.",
                "Cached news body.",
                new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
                null,
                true);
            return Task.FromResult<IReadOnlyList<NewsItem>>([item]);
        }

        public Task<NewsArchiveYearRange> GetArchiveYearRangeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new NewsArchiveYearRange(null, null));

        public Task<NewsItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<NewsItem?>(null);

        public Task<IReadOnlyList<NewsItem>> GetByIdsAsync(
            IReadOnlyCollection<int> ids,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NewsItem>>([]);

        public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SitemapContentEntry>>([]);

        public Task<NewsSearchPage> SearchAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new NewsSearchPage([], 0, page, pageSize));
    }

    private class CountingArticlesRepository : IArticlesRepository
    {
        private readonly ArticleItem item = new(
            1,
            "Cached article",
            "Cached article excerpt.",
            "Cached article body.",
            new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
            null,
            null,
            true);

        public int PublishedCountCallCount { get; private set; }

        public int LatestCallCount { get; private set; }

        public int ArchivePageCallCount { get; private set; }

        public virtual Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default)
        {
            PublishedCountCallCount++;
            return Task.FromResult(1);
        }

        public virtual Task<IReadOnlyList<ArticleItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default)
        {
            LatestCallCount++;
            return Task.FromResult<IReadOnlyList<ArticleItem>>([item]);
        }

        public Task<IReadOnlyList<ArticleItem>> GetArchivePageAsync(int page, int pageSize, CancellationToken cancellationToken = default)
        {
            ArchivePageCallCount++;
            return Task.FromResult<IReadOnlyList<ArticleItem>>([
                item with { Id = page, Title = $"Cached article page {page}:{pageSize}" }
            ]);
        }

        public Task<ArticleItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ArticleItem?>(id == item.Id ? item : null);

        public Task<IReadOnlyList<SitemapContentEntry>> GetPublishedSitemapEntriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SitemapContentEntry>>([new SitemapContentEntry(item.Id, item.Title, item.PublishedAt)]);
    }

    private class CountingForumRepository : IForumRepository
    {
        private readonly ForumCategoryItem category = new(
            1,
            "Cached forum",
            "Cached forum description.",
            12,
            new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc),
            "Cached thread",
            1);

        public int CategoriesCallCount { get; private set; }

        public int ThreadCountCallCount { get; private set; }

        public int RecentThreadsCallCount { get; private set; }

        public virtual Task<IReadOnlyList<ForumCategoryItem>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        {
            CategoriesCallCount++;
            return Task.FromResult<IReadOnlyList<ForumCategoryItem>>([category]);
        }

        public virtual Task<int> GetTotalThreadCountAsync(CancellationToken cancellationToken = default)
        {
            ThreadCountCallCount++;
            return Task.FromResult(4);
        }

        public virtual Task<IReadOnlyList<ForumRecentThreadItem>> GetRecentThreadsAsync(
            int count,
            CancellationToken cancellationToken = default)
        {
            RecentThreadsCallCount++;
            return Task.FromResult<IReadOnlyList<ForumRecentThreadItem>>([
                new ForumRecentThreadItem(
                    1001,
                    "Cached recent thread",
                    category.Id,
                    category.Name,
                    3,
                    category.LastActivityAt ?? DateTime.UtcNow)
            ]);
        }

        public Task<IReadOnlyList<ForumRecentThreadItem>> GetLegacyDiscographyThreadsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ForumRecentThreadItem>>([]);

        public Task<ForumArchiveStats> GetArchiveStatsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumArchiveStats(1, 4, 12));

        public Task<ForumCategoryItem?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumCategoryItem?>(id == category.Id ? category : null);

        public Task<ForumCategoryTopicsPage> GetCategoryTopicsPageAsync(int forumId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ForumTopicPostsPage?> GetTopicPostsPageAsync(int topicId, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> GetTopicSitemapCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<ForumTopicSitemapItem>> GetTopicSitemapPageAsync(int offset, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ForumTopicSitemapItem>>([]);

        public Task<ForumSearchPage> SearchForumAsync(string query, int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private class CountingQueenHistoryRepository : IQueenHistoryRepository
    {
        public int OnThisDayCallCount { get; private set; }

        public int AroundThisDayCallCount { get; private set; }

        public int AllPublishedCallCount { get; private set; }

        public virtual Task<IReadOnlyList<QueenHistoryEvent>> GetOnThisDayAsync(DateOnly date, int count, CancellationToken cancellationToken = default)
        {
            OnThisDayCallCount++;
            return Task.FromResult<IReadOnlyList<QueenHistoryEvent>>([CreateEvent($"on-this-day:{date:yyyy-MM-dd}:{count}")]);
        }

        public virtual Task<IReadOnlyList<QueenHistoryEvent>> GetAroundThisDayAsync(
            DateOnly date,
            int dayWindow,
            int count,
            CancellationToken cancellationToken = default)
        {
            AroundThisDayCallCount++;
            return Task.FromResult<IReadOnlyList<QueenHistoryEvent>>(
                [CreateEvent($"around-this-day:{date:yyyy-MM-dd}:{dayWindow}:{count}")]);
        }

        public Task<IReadOnlyList<QueenHistoryEvent>> GetAllPublishedAsync(CancellationToken cancellationToken = default)
        {
            AllPublishedCallCount++;
            return Task.FromResult<IReadOnlyList<QueenHistoryEvent>>([CreateEvent("all-published")]);
        }

        private static QueenHistoryEvent CreateEvent(string title) =>
            new(
                1,
                title,
                "Cached history summary.",
                new DateTime(1985, 7, 13, 0, 0, 0, DateTimeKind.Utc),
                QueenHistoryDatePrecision.ExactDate,
                QueenHistoryEventCategory.Concert,
                100,
                QueenHistoryEventSourceType.Curated,
                "cached-history",
                null,
                true);
    }

    private sealed class CountingFanPerformanceRepository : IFanPerformanceRepository
    {
        public int PageCallCount { get; private set; }

        public int CountCallCount { get; private set; }

        public int ByIdCallCount { get; private set; }

        public Task<IReadOnlyList<FanPerformance>> GetPageAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            PageCallCount++;
            return Task.FromResult<IReadOnlyList<FanPerformance>>(SampleFanPerformanceData.CreateSeedPerformances());
        }

        public Task<int> GetVisibleCountAsync(CancellationToken cancellationToken = default)
        {
            CountCallCount++;
            return Task.FromResult(4);
        }

        public Task<FanPerformance?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            ByIdCallCount++;
            return Task.FromResult(SampleFanPerformanceData.CreateSeedPerformances().FirstOrDefault(item => item.Id == id));
        }
    }

    private class CountingPhotoRepository : IPhotoRepository
    {
        public int CategoriesCallCount { get; protected set; }

        public int PageCallCount { get; private set; }

        public virtual Task<IReadOnlyList<PhotoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        {
            CategoriesCallCount++;
            return Task.FromResult<IReadOnlyList<PhotoCategory>>(
            [
                new PhotoCategory(9, "Brian May", "brian-may", 3, "https://cdn.queenzone.org/brian-may/cover.jpg"),
            ]);
        }

        public Task<PhotoCategory?> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PhotoCategoryPage> GetCategoryPageAsync(
            int catId,
            int page,
            int pageSize,
            PhotoListFilter? filter = null,
            CancellationToken cancellationToken = default)
        {
            PageCallCount++;
            return Task.FromResult(new PhotoCategoryPage(
                "Brian May",
                [
                    new PhotoItem(
                        101,
                        catId,
                        "Brian May",
                        "brian-may",
                        $"page-{page}",
                        "https://cdn.queenzone.org/brian-may/a.jpg",
                        "https://cdn.queenzone.org/brian-may/a-t.jpg",
                        150,
                        150,
                        1920,
                        1080,
                        1986,
                        new DateTime(1986, 7, 12)),
                ],
                3));
        }

        public Task<PhotoDetailNavigation?> GetDetailNavigationAsync(
            int catId,
            int picId,
            PhotoListFilter? filter = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PhotoItem>> GetCategoryAllAsync(int catId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PhotoItem>> GetRandomPublishedInCategoryAsync(
            int catId,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public virtual Task<IReadOnlyList<int>> PickRandomPublishedPhotoIdsAsync(
            int catId,
            int take,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public virtual Task<IReadOnlyList<PhotoItem>> GetPublishedByIdsAsync(
            int catId,
            IReadOnlyList<int> picIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PhotoSitemapCategory>> GetPublishedSitemapCategoriesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private class CountingQuoteRepository(params QuoteItem[] quotes) : IQuoteRepository
    {
        public int AllCallCount { get; private set; }

        public virtual Task<IReadOnlyList<QuoteItem>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            AllCallCount++;
            return Task.FromResult<IReadOnlyList<QuoteItem>>(quotes);
        }

        public Task<QuoteItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(quotes.FirstOrDefault(quote => quote.Id == id));

        public Task<QuoteItem?> GetRandomPublishedAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CreateAsync(AdminQuoteDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(int id, AdminQuoteDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetPublishedAsync(int id, bool isPublished, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SlowCountingQuoteRepository(TimeSpan delay, params QuoteItem[] quotes)
        : CountingQuoteRepository(quotes)
    {
        public override async Task<IReadOnlyList<QuoteItem>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return await base.GetAllAsync(cancellationToken);
        }
    }

    private sealed class CountingTriviaRepository(params TriviaFactItem[] facts) : ITriviaRepository
    {
        public int AllCallCount { get; private set; }

        public Task<IReadOnlyList<TriviaFactItem>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            AllCallCount++;
            return Task.FromResult<IReadOnlyList<TriviaFactItem>>(facts);
        }

        public Task<TriviaFactItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(facts.FirstOrDefault(fact => fact.Id == id));

        public Task<TriviaFactItem?> GetRandomPublishedAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CreateAsync(AdminTriviaDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(int id, AdminTriviaDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetPublishedAsync(int id, bool isPublished, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingBiographyRepository : IBiographyRepository
    {
        private readonly BiographyChapterItem chapter = new(
            1,
            "Cached chapter",
            "Cached summary.",
            "Cached body.",
            1,
            new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc));

        public int ChaptersCallCount { get; private set; }

        public Task<IReadOnlyList<BiographyChapterItem>> GetChaptersAsync(CancellationToken cancellationToken = default)
        {
            ChaptersCallCount++;
            return Task.FromResult<IReadOnlyList<BiographyChapterItem>>([chapter]);
        }

        public Task<BiographyChapterItem?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult<BiographyChapterItem?>(id == chapter.Id ? chapter : null);

        public Task<BiographyChapterNav> GetAdjacentChaptersAsync(int id, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BiographyChapterNav(null, null));

        public Task<int> CreateAsync(AdminBiographyDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdateAsync(int id, AdminBiographyDraft draft, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingDiscographyRepository : IDiscographyRepository
    {
        private readonly AlbumSummary album = new(1, "Cached album", "cached-album", 1975, null);

        public int AlbumsCallCount { get; private set; }

        public Task<IReadOnlyList<AlbumSummary>> GetAlbumsAsync(CancellationToken cancellationToken = default)
        {
            AlbumsCallCount++;
            return Task.FromResult<IReadOnlyList<AlbumSummary>>([album]);
        }

        public Task<AlbumDetail?> GetAlbumByIdAsync(int albumId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AlbumDetail?>(null);
    }

    private sealed class ConcurrentEntryGate(int expected)
    {
        private readonly TaskCompletionSource allEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int entered;

        public int Entered => Volatile.Read(ref entered);

        public async Task EnterAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref entered) == expected)
            {
                allEntered.TrySetResult();
            }

            await allEntered.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class BarrierNewsRepository(ConcurrentEntryGate gate) : CountingNewsRepository
    {
        public override async Task<IReadOnlyList<NewsItem>> GetLatestAsync(
            int count,
            CancellationToken cancellationToken = default)
        {
            await gate.EnterAsync(cancellationToken);
            return await base.GetLatestAsync(count, cancellationToken);
        }

        public override async Task<int> GetPublishedCountAsync(NewsArchiveFilter filter = default, CancellationToken cancellationToken = default)
        {
            await gate.EnterAsync(cancellationToken);
            return await base.GetPublishedCountAsync(filter, cancellationToken);
        }
    }

    private sealed class BarrierArticlesRepository(ConcurrentEntryGate gate) : CountingArticlesRepository
    {
        public override async Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default)
        {
            await gate.EnterAsync(cancellationToken);
            return await base.GetPublishedCountAsync(cancellationToken);
        }

        public override async Task<IReadOnlyList<ArticleItem>> GetLatestAsync(int count, CancellationToken cancellationToken = default)
        {
            await gate.EnterAsync(cancellationToken);
            return await base.GetLatestAsync(count, cancellationToken);
        }
    }

    private sealed class BarrierForumRepository(ConcurrentEntryGate gate) : CountingForumRepository
    {
        public override async Task<IReadOnlyList<ForumCategoryItem>> GetCategoriesAsync(
            CancellationToken cancellationToken = default)
        {
            await gate.EnterAsync(cancellationToken);
            return await base.GetCategoriesAsync(cancellationToken);
        }

        public override async Task<int> GetTotalThreadCountAsync(CancellationToken cancellationToken = default)
        {
            await gate.EnterAsync(cancellationToken);
            return await base.GetTotalThreadCountAsync(cancellationToken);
        }

        public override async Task<IReadOnlyList<ForumRecentThreadItem>> GetRecentThreadsAsync(
            int count,
            CancellationToken cancellationToken = default)
        {
            await gate.EnterAsync(cancellationToken);
            return await base.GetRecentThreadsAsync(count, cancellationToken);
        }
    }

    private sealed class BarrierQueenHistoryRepository(ConcurrentEntryGate gate) : CountingQueenHistoryRepository
    {
        public override async Task<IReadOnlyList<QueenHistoryEvent>> GetOnThisDayAsync(
            DateOnly date,
            int count,
            CancellationToken cancellationToken = default)
        {
            await gate.EnterAsync(cancellationToken);
            return await base.GetOnThisDayAsync(date, count, cancellationToken);
        }

        public override async Task<IReadOnlyList<QueenHistoryEvent>> GetAroundThisDayAsync(
            DateOnly date,
            int dayWindow,
            int count,
            CancellationToken cancellationToken = default)
        {
            await gate.EnterAsync(cancellationToken);
            return await base.GetAroundThisDayAsync(date, dayWindow, count, cancellationToken);
        }
    }

    private sealed class BarrierPhotoRepository(ConcurrentEntryGate gate) : CountingPhotoRepository
    {
        public override async Task<IReadOnlyList<PhotoCategory>> GetCategoriesAsync(
            CancellationToken cancellationToken = default)
        {
            await gate.EnterAsync(cancellationToken);
            return await base.GetCategoriesAsync(cancellationToken);
        }
    }

    private sealed class UnusedFreddieTributeRepository : IFreddieTributeRepository
    {
        public Task<FreddieTributePage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FreddieTribute?> GetRandomAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int?> PickRandomVisibleIdAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FreddieTribute?> GetVisibleByIdAsync(int id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CountingFreddieTributeRepository(FreddieTribute tribute) : IFreddieTributeRepository
    {
        public int PickCallCount { get; private set; }

        public int ByIdCallCount { get; private set; }

        public Task<FreddieTributePage> GetPageAsync(int page, int pageSize, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FreddieTribute?> GetRandomAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int?> PickRandomVisibleIdAsync(CancellationToken cancellationToken = default)
        {
            PickCallCount++;
            return Task.FromResult<int?>(tribute.Id);
        }

        public Task<FreddieTribute?> GetVisibleByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            ByIdCallCount++;
            return Task.FromResult<FreddieTribute?>(id == tribute.Id ? tribute : null);
        }
    }

    private sealed class CountingFreddiePhotoRepository : CountingPhotoRepository
    {
        public int PickCallCount { get; private set; }

        public int ByIdCallCount { get; private set; }

        public override Task<IReadOnlyList<PhotoCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
        {
            CategoriesCallCount++;
            return Task.FromResult<IReadOnlyList<PhotoCategory>>(
            [
                new PhotoCategory(18, "Freddie Mercury", "freddie-mercury", 4, null),
            ]);
        }

        public override Task<IReadOnlyList<int>> PickRandomPublishedPhotoIdsAsync(
            int catId,
            int take,
            CancellationToken cancellationToken = default)
        {
            PickCallCount++;
            IReadOnlyList<int> ids = [301, 302, 303, 304];
            return Task.FromResult(ids);
        }

        public override Task<IReadOnlyList<PhotoItem>> GetPublishedByIdsAsync(
            int catId,
            IReadOnlyList<int> picIds,
            CancellationToken cancellationToken = default)
        {
            ByIdCallCount++;
            IReadOnlyList<PhotoItem> items = picIds
                .Select(id => new PhotoItem(
                    id,
                    catId,
                    "Freddie Mercury",
                    "freddie-mercury",
                    $"Photo {id}",
                    $"https://cdn.queenzone.org/freddie-mercury/{id}.jpg",
                    $"https://cdn.queenzone.org/freddie-mercury/{id}-t.jpg",
                    100,
                    100,
                    800,
                    600,
                    1986,
                    new DateTime(1986, 7, 12)))
                .ToList();
            return Task.FromResult(items);
        }
    }
}
