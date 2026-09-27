using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Search.Shared;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web.Tests;

public sealed class AdminFanPerformanceWriteServiceTests
{
    [Fact]
    public async Task Create_Visible_AppearsInPublicReads_AndUpsertsFanPerformanceSearchDocument()
    {
        var harness = CreateHarness();

        var id = await harness.Service.CreateAsync(
            NewCreateRequest("Live publish", isVisible: true),
            "admin@test.local");

        var publicItem = await harness.PublicRepository.GetByIdAsync(id);
        Assert.NotNull(publicItem);
        Assert.Equal("Live publish", publicItem.Title);

        var documents = harness.SearchStore.GetAll();
        var document = Assert.Single(documents);
        Assert.Equal(SiteSearchContentType.FanPerformance, document.ContentType);
        Assert.Equal(SearchReindexBuilder.FanPerformanceSourceKey(id), document.SourceKey);
        Assert.Equal("Live publish", document.Title);
        Assert.Contains(PublicOutputCachePolicies.PublicHtmlTag, harness.OutputCache.EvictedTags);
        Assert.Contains(PublicOutputCachePolicies.PublicSitemapTag, harness.OutputCache.EvictedTags);
    }

    [Fact]
    public async Task Hide_RemovesFromPublicReadsAndSearch_WithoutDeletingAudioBlobName()
    {
        var harness = CreateHarness();
        var id = await harness.Service.CreateAsync(
            NewCreateRequest("Hide me", isVisible: true, audioFileName: "survive.mp3"),
            "admin@test.local");

        await harness.Service.HideAsync(id, "admin@test.local");

        Assert.Null(await harness.PublicRepository.GetByIdAsync(id));
        var hidden = await harness.AdminRepository.GetByIdAsync(id);
        Assert.NotNull(hidden);
        Assert.False(hidden.IsVisible);
        Assert.Equal("survive.mp3", hidden.AudioFileName);
        Assert.Empty(harness.SearchStore.GetAll());
    }

    [Fact]
    public async Task Create_InvalidatesFanPerformanceQueryCache()
    {
        var store = new SharedFanPerformanceStore();
        var publicRepository = new CountingFanPerformanceRepository(new InMemoryFanPerformanceRepository(store));
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var queryCache = CreateQueryCache(memoryCache, publicRepository);
        var outputCache = new RecordingOutputCacheStore();
        var searchStore = new SharedSearchIndexStore();
        var service = new AdminFanPerformanceWriteService(
            new InMemoryAdminFanPerformanceRepository(store),
            queryCache,
            CreateSitemapService(outputCache, memoryCache),
            outputCache,
            new InMemorySearchIndexService(searchStore),
            NullLogger<AdminFanPerformanceWriteService>.Instance);

        _ = await queryCache.GetFanPerformanceVisibleCountAsync();
        Assert.Equal(1, publicRepository.CountCallCount);

        await service.CreateAsync(NewCreateRequest("Cache bust"), "admin@test.local");

        _ = await queryCache.GetFanPerformanceVisibleCountAsync();
        Assert.Equal(2, publicRepository.CountCallCount);
    }

    [Fact]
    public async Task Update_StaleConcurrencyToken_DoesNotMutateOrReindex()
    {
        var harness = CreateHarness();
        var id = await harness.Service.CreateAsync(NewCreateRequest("Original"), "admin@test.local");
        var created = await harness.AdminRepository.GetByIdAsync(id);
        var stale = created!.ToConcurrencyToken();

        await harness.Service.UpdateAsync(
            id,
            new AdminFanPerformanceUpdateRequest("Changed", created.PerformedBy, created.Description, created.DateAdded),
            "admin@test.local");

        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            harness.Service.UpdateAsync(
                id,
                new AdminFanPerformanceUpdateRequest("Stale", created.PerformedBy, created.Description, created.DateAdded),
                "admin@test.local",
                stale));

        Assert.Equal("Changed", (await harness.AdminRepository.GetByIdAsync(id))!.Title);
        Assert.Equal("Changed", Assert.Single(harness.SearchStore.GetAll()).Title);
    }

    [Fact]
    public async Task SearchSyncFailure_DoesNotFailTheWrite()
    {
        var store = new SharedFanPerformanceStore();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var outputCache = new RecordingOutputCacheStore();
        var service = new AdminFanPerformanceWriteService(
            new InMemoryAdminFanPerformanceRepository(store),
            CreateQueryCache(memoryCache, new InMemoryFanPerformanceRepository(store)),
            CreateSitemapService(outputCache, memoryCache),
            outputCache,
            new ThrowingSearchIndexService(),
            NullLogger<AdminFanPerformanceWriteService>.Instance);

        var id = await service.CreateAsync(NewCreateRequest("Still saved"), "admin@test.local");

        Assert.Equal("Still saved", (await new InMemoryAdminFanPerformanceRepository(store).GetByIdAsync(id))!.Title);
    }

    private static AdminFanPerformanceCreateRequest NewCreateRequest(
        string title,
        bool isVisible = true,
        string audioFileName = "new-row.mp3") =>
        new(
            title,
            "Test Performer",
            "Notes",
            audioFileName,
            4096,
            new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc),
            isVisible);

    private static WriteHarness CreateHarness()
    {
        var store = new SharedFanPerformanceStore();
        var admin = new InMemoryAdminFanPerformanceRepository(store);
        var publicRepo = new InMemoryFanPerformanceRepository(store);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var outputCache = new RecordingOutputCacheStore();
        var searchStore = new SharedSearchIndexStore();
        var service = new AdminFanPerformanceWriteService(
            admin,
            CreateQueryCache(memoryCache, publicRepo),
            CreateSitemapService(outputCache, memoryCache),
            outputCache,
            new InMemorySearchIndexService(searchStore),
            NullLogger<AdminFanPerformanceWriteService>.Instance);
        return new WriteHarness(service, admin, publicRepo, searchStore, outputCache);
    }

    private static PublicQueryCacheService CreateQueryCache(
        IMemoryCache memoryCache,
        IFanPerformanceRepository fanPerformanceRepository) =>
        new(
            memoryCache,
            Options.Create(new PublicQueryCacheOptions()),
            new InMemoryNewsRepository(new SharedNewsStore(SampleNewsData.CreateSeedArticles())),
            new InMemoryArticlesRepository(SampleArticlesData.CreateSeedArticles()),
            new InMemoryArticleRepository(new InMemoryArticleSubmissionRepository()),
            new InMemoryForumRepository(SampleForumData.CreateSeedCategories(), SampleForumData.CreateSeedStats()),
            new InMemoryQueenHistoryRepository(SampleQueenHistoryData.CreateSeedEvents()),
            new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories())),
            new StubLiveActivityQueryService(),
            fanPerformanceRepository,
            new InMemoryQuoteRepository(SampleQuoteData.CreateSeedQuotes()),
            new InMemoryTriviaRepository(SampleTriviaData.CreateSeedFacts()),
            new InMemoryBiographyRepository(SampleBiographyData.CreateSeedChapters()),
            new InMemoryDiscographyRepository(SampleDiscographyData.CreateSeedAlbums()),
            new InMemoryFreddieTributeRepository(new SharedFreddieTributeStore(SampleFreddieTributeData.CreateSeedTributes())));

    private static CoreSitemapService CreateSitemapService(IOutputCacheStore outputCache, IMemoryCache cache) =>
        new(
            new CoreSitemapBuilder(
                new InMemoryNewsRepository(new SharedNewsStore(SampleNewsData.CreateSeedArticles())),
                new InMemoryArticlesRepository(SampleArticlesData.CreateSeedArticles()),
                new InMemoryArticleRepository(new InMemoryArticleSubmissionRepository()),
                new InMemoryBiographyRepository(SampleBiographyData.CreateSeedChapters()),
                new InMemoryForumRepository(SampleForumData.CreateSeedCategories(), SampleForumData.CreateSeedStats()),
                new InMemoryPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories())),
                new InMemoryFanPerformanceRepository([]),
                new InMemoryDiscographyRepository(SampleDiscographyData.CreateSeedAlbums())),
            cache,
            Options.Create(new SitemapOptions()),
            outputCache);

    private sealed record WriteHarness(
        AdminFanPerformanceWriteService Service,
        IAdminFanPerformanceRepository AdminRepository,
        IFanPerformanceRepository PublicRepository,
        SharedSearchIndexStore SearchStore,
        RecordingOutputCacheStore OutputCache);

    private sealed class RecordingOutputCacheStore : IOutputCacheStore
    {
        public List<string> EvictedTags { get; } = [];

        public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
        {
            EvictedTags.Add(tag);
            return ValueTask.CompletedTask;
        }

        public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
            ValueTask.FromResult<byte[]?>(null);

        public ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class CountingFanPerformanceRepository(IFanPerformanceRepository inner) : IFanPerformanceRepository
    {
        public int CountCallCount { get; private set; }

        public Task<IReadOnlyList<FanPerformance>> GetPageAsync(
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.GetPageAsync(page, pageSize, cancellationToken);

        public Task<int> GetVisibleCountAsync(CancellationToken cancellationToken = default)
        {
            CountCallCount++;
            return inner.GetVisibleCountAsync(cancellationToken);
        }

        public Task<FanPerformance?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
            inner.GetByIdAsync(id, cancellationToken);
    }

    private sealed class StubLiveActivityQueryService : ILiveActivityQueryService
    {
        public Task<int> GetNewForumRepliesTodayAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class ThrowingSearchIndexService : ISearchIndexService
    {
        public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Search index unavailable.");

        public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Search index unavailable.");

        public Task ReplaceContentTypeAsync(
            string contentType,
            IReadOnlyList<SearchDocumentEntity> documents,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Search index unavailable.");

        public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Search index unavailable.");
    }
}

public sealed class AdminFanPerformancePublishRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public AdminFanPerformancePublishRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Publish_AppearsOnArchivePageAndContentApi_WithoutRestart()
    {
        using var scope = factory.Services.CreateScope();
        var writeService = scope.ServiceProvider.GetRequiredService<AdminFanPerformanceWriteService>();
        var search = scope.ServiceProvider.GetRequiredService<ISearchIndexService>();
        var title = $"Write-path publish {Guid.NewGuid():N}";

        var id = await writeService.CreateAsync(
            new AdminFanPerformanceCreateRequest(
                title,
                "Route Tester",
                "Published through the admin write path.",
                "write-path.mp3",
                1024,
                DateTime.UtcNow,
                IsVisible: true),
            "admin@test.local");

        using var client = factory.CreateAnonymousClient();
        var html = await client.GetStringAsync("/fan-performances");
        Assert.Contains(title, html, StringComparison.Ordinal);

        using var apiResponse = await client.GetAsync("/api/v1/content/fan-performances");
        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);
        var payload = await apiResponse.Content.ReadFromJsonAsync<ApiPagedResponse<FanPerformanceDto>>();
        Assert.NotNull(payload);
        Assert.Contains(payload.Items, item => item.Id == id && item.Title == title);

        using var detailResponse = await client.GetAsync($"/api/v1/content/fan-performances/{id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var counts = await search.GetContentTypeCountsAsync();
        Assert.True(counts.TryGetValue(SiteSearchContentType.FanPerformance, out var indexed));
        Assert.True(indexed >= 1);
    }

    [Fact]
    public async Task Hide_DropsPublicSurfaces_AndLeavesAdminRow()
    {
        using var scope = factory.Services.CreateScope();
        var writeService = scope.ServiceProvider.GetRequiredService<AdminFanPerformanceWriteService>();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminFanPerformanceRepository>();
        var title = $"Write-path hide {Guid.NewGuid():N}";

        var id = await writeService.CreateAsync(
            new AdminFanPerformanceCreateRequest(
                title,
                "Route Tester",
                "Hidden after publish.",
                "write-path-hide.mp3",
                1024,
                DateTime.UtcNow,
                IsVisible: true),
            "admin@test.local");

        await writeService.HideAsync(id, "admin@test.local");

        using var client = factory.CreateAnonymousClient();
        var html = await client.GetStringAsync("/fan-performances");
        Assert.DoesNotContain(title, html, StringComparison.Ordinal);

        using var apiResponse = await client.GetAsync($"/api/v1/content/fan-performances/{id}");
        Assert.Equal(HttpStatusCode.NotFound, apiResponse.StatusCode);

        var hidden = await admin.GetByIdAsync(id);
        Assert.NotNull(hidden);
        Assert.False(hidden.IsVisible);
        Assert.Equal("write-path-hide.mp3", hidden.AudioFileName);
    }
}
