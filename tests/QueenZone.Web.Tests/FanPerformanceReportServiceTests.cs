using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Search.Shared;
using QueenZone.Web;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web.Tests;

public sealed class FanPerformanceReportServiceTests
{
    [Fact]
    public async Task CreateAsync_IsIdempotent_ForOneOpenReportPerMemberAndStage()
    {
        var reporter = Guid.NewGuid();
        var store = new SharedFanPerformanceStore(SampleFanPerformanceData.CreateSeedPerformances());
        var reports = new InMemoryFanPerformanceReportRepository();
        var publicRepo = new InMemoryFanPerformanceRepository(store);
        var service = CreateService(reports, publicRepo, store);

        var first = await service.CreateAsync(reporter, 187, "Rights issue");
        var second = await service.CreateAsync(reporter, 187, "Still a rights issue");

        Assert.True(first.Succeeded);
        Assert.False(first.AlreadyReported);
        Assert.True(second.Succeeded);
        Assert.True(second.AlreadyReported);
        Assert.Equal(first.ReportId, second.ReportId);
        Assert.Equal(1, await reports.CountOpenAsync());
    }

    [Fact]
    public async Task HideAndResolveAsync_SetsDisplayOff_AndMarksResolved()
    {
        var reporter = Guid.NewGuid();
        var store = new SharedFanPerformanceStore(SampleFanPerformanceData.CreateSeedPerformances());
        var reports = new InMemoryFanPerformanceReportRepository();
        var publicRepo = new InMemoryFanPerformanceRepository(store);
        var adminRepo = new InMemoryAdminFanPerformanceRepository(store);
        var service = CreateService(reports, publicRepo, store);

        var created = await service.CreateAsync(reporter, 187, "Hide this");
        Assert.True(created.Succeeded);

        var resolved = await service.HideAndResolveAsync(created.ReportId!.Value, "admin@test.local");

        Assert.NotNull(resolved);
        Assert.Equal(FanPerformanceReportStatus.Resolved, resolved!.Status);
        Assert.Equal("admin@test.local", resolved.ReviewedBy);
        Assert.Null(await publicRepo.GetByIdAsync(187));
        var adminItem = await adminRepo.GetByIdAsync(187);
        Assert.NotNull(adminItem);
        Assert.False(adminItem!.IsVisible);
        Assert.False(string.IsNullOrWhiteSpace(adminItem.AudioFileName));
    }

    [Fact]
    public async Task DismissAsync_LeavesPublishedRowVisible()
    {
        var reporter = Guid.NewGuid();
        var store = new SharedFanPerformanceStore(SampleFanPerformanceData.CreateSeedPerformances());
        var reports = new InMemoryFanPerformanceReportRepository();
        var publicRepo = new InMemoryFanPerformanceRepository(store);
        var service = CreateService(reports, publicRepo, store);

        var created = await service.CreateAsync(reporter, 186, "Not actually a problem");
        var dismissed = await service.DismissAsync(created.ReportId!.Value, "admin@test.local");

        Assert.Equal(FanPerformanceReportStatus.Dismissed, dismissed!.Status);
        Assert.NotNull(await publicRepo.GetByIdAsync(186));
    }

    private static FanPerformanceReportService CreateService(
        IFanPerformanceReportRepository reports,
        IFanPerformanceRepository publicRepo,
        SharedFanPerformanceStore store)
    {
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var outputCache = new NoOpOutputCacheStore();
        var write = new AdminFanPerformanceWriteService(
            new InMemoryAdminFanPerformanceRepository(store),
            new PublicQueryCacheService(
                memoryCache,
                Options.Create(new PublicQueryCacheOptions()),
                new InMemoryNewsRepository(new SharedNewsStore(SampleNewsData.CreateSeedArticles())),
                new InMemoryArticlesRepository(SampleArticlesData.CreateSeedArticles()),
                new InMemoryArticleRepository(new InMemoryArticleSubmissionRepository()),
                new InMemoryForumRepository(SampleForumData.CreateSeedCategories(), SampleForumData.CreateSeedStats()),
                new InMemoryQueenHistoryRepository(SampleQueenHistoryData.CreateSeedEvents()),
                new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories())),
                new StubLiveActivityQueryService(),
                publicRepo,
                new InMemoryQuoteRepository(SampleQuoteData.CreateSeedQuotes()),
                new InMemoryTriviaRepository(SampleTriviaData.CreateSeedFacts()),
                new InMemoryBiographyRepository(SampleBiographyData.CreateSeedChapters()),
                new InMemoryDiscographyRepository(SampleDiscographyData.CreateSeedAlbums()),
                new InMemoryFreddieTributeRepository(new SharedFreddieTributeStore(SampleFreddieTributeData.CreateSeedTributes()))),
            new CoreSitemapService(
                new CoreSitemapBuilder(
                    new InMemoryNewsRepository(new SharedNewsStore(SampleNewsData.CreateSeedArticles())),
                    new InMemoryArticlesRepository(SampleArticlesData.CreateSeedArticles()),
                    new InMemoryArticleRepository(new InMemoryArticleSubmissionRepository()),
                    new InMemoryBiographyRepository(SampleBiographyData.CreateSeedChapters()),
                    new InMemoryForumRepository(SampleForumData.CreateSeedCategories(), SampleForumData.CreateSeedStats()),
                    new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories())),
                    publicRepo,
                    new InMemoryDiscographyRepository(SampleDiscographyData.CreateSeedAlbums())),
                memoryCache,
                Options.Create(new SitemapOptions()),
                outputCache),
            outputCache,
            new NoOpSearchIndexService(),
            NullLogger<AdminFanPerformanceWriteService>.Instance);
        return new FanPerformanceReportService(reports, publicRepo, write);
    }

    private sealed class StubLiveActivityQueryService : ILiveActivityQueryService
    {
        public Task<int> GetNewForumRepliesTodayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class NoOpSearchIndexService : ISearchIndexService
    {
        public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ReplaceContentTypeAsync(
            string contentType,
            IReadOnlyList<SearchDocumentEntity> documents,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
    }

    private sealed class NoOpOutputCacheStore : IOutputCacheStore
    {
        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
            ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(
            string key,
            byte[] value,
            string[]? tags,
            TimeSpan validFor,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
