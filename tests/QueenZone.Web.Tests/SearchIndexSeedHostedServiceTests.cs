using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web.Search;

namespace QueenZone.Web.Tests;

public sealed class SearchIndexSeedHostedServiceTests
{
    [Fact]
    public async Task StartAsync_starts_SearchIndexSeed_activity_during_scoped_work()
    {
        var recorder = new RecordingSearchIndexService();
        var services = new ServiceCollection();
        services.AddSingleton<ISearchIndexService>(recorder);
        services.AddSingleton<INewsRepository>(new InMemoryNewsRepository(new SharedNewsStore(SampleNewsData.CreateSeedArticles())));
        services.AddSingleton<IForumRepository>(new InMemoryForumRepository(
            SampleForumData.CreateSeedCategories(),
            SampleForumData.CreateSeedStats(),
            new InMemoryForumWriteRepository(),
            new InMemoryForumAttachmentRepository()));
        services.AddSingleton<IArticleRepository>(new InMemoryArticleRepository(new InMemoryArticleSubmissionRepository()));
        services.AddSingleton<IArticlesRepository>(new InMemoryArticlesRepository(SampleArticlesData.CreateSeedArticles()));
        services.AddSingleton<IBiographyRepository>(new InMemoryBiographyRepository(SampleBiographyData.CreateSeedChapters()));
        services.AddSingleton<IDiscographyRepository>(new InMemoryDiscographyRepository(SampleDiscographyData.CreateSeedAlbums()));
        services.AddSingleton<IQueenHistoryRepository>(new InMemoryQueenHistoryRepository(SampleQueenHistoryData.CreateSeedEvents()));
        services.AddSingleton<IFanPerformanceRepository>(new InMemoryFanPerformanceRepository(SampleFanPerformanceData.CreateSeedPerformances()));
        services.AddTransient<CatalogSearchReindexer>();
        services.AddTransient<SearchReindexBuilder>();
        await using var provider = services.BuildServiceProvider();

        using var listener = QueenZoneActivityTestListener.Listen();
        var hosted = new SearchIndexSeedHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SearchIndexSeedHostedService>.Instance);

        await hosted.StartAsync(CancellationToken.None);

        Assert.True(recorder.ReplaceCalls > 0);
        var activity = Assert.Single(listener.Started, item => item.OperationName == "SearchIndexSeed");
        Assert.Equal(ActivityKind.Internal, activity.Kind);
        Assert.NotNull(recorder.ActivityDuringWork);
        Assert.Equal("SearchIndexSeed", recorder.ActivityDuringWork.OperationName);
        Assert.Equal(activity.Id, recorder.ActivityDuringWork.Id);
        Assert.True(activity.IsStopped);
        Assert.Contains(listener.Stopped, item => item.Id == activity.Id);
    }

    private sealed class RecordingSearchIndexService : ISearchIndexService
    {
        public int ReplaceCalls;

        public Activity? ActivityDuringWork;

        public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ReplaceContentTypeAsync(
            string contentType,
            IReadOnlyList<SearchDocumentEntity> documents,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ReplaceCalls);
            ActivityDuringWork = Activity.Current;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
    }
}
