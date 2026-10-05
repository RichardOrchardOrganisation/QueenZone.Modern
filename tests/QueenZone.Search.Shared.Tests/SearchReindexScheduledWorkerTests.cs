using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Search.Shared;

namespace QueenZone.Search.Shared.Tests;

public sealed class SearchReindexScheduledWorkerTests
{
    [Fact]
    public async Task RunAsync_reindexes_and_completes_the_request()
    {
        var leaseStore = new SharedSearchReindexLeaseStore();
        var requestStore = new SharedSearchReindexRunRequestStore();
        var worker = CreateWorker(leaseStore, requestStore);

        var exitCode = await worker.RunAsync(new SearchReindexCommandOptions(Force: false));

        Assert.Equal(0, exitCode);
        Assert.NotNull(worker.LastRunSummary);
        Assert.False(worker.LastRunSummary!.SkippedDueToLease);
        Assert.False(worker.LastRunSummary!.SkippedNoClaim);

        var requestRepository = new InMemorySearchReindexRunRequestRepository(requestStore);
        var recent = await requestRepository.ListRecentAsync();
        Assert.Single(recent);
        Assert.Equal(SearchReindexRunRequestStatus.Completed, recent[0].Status);
    }

    [Fact]
    public async Task RunAsync_skips_when_run_lease_is_held()
    {
        var leaseStore = new SharedSearchReindexLeaseStore();
        var requestStore = new SharedSearchReindexRunRequestStore();
        var leaseService = new InMemorySearchReindexRunLeaseService(leaseStore);
        await using var held = (await leaseService.TryAcquireAsync("search-reindex", TimeSpan.FromMinutes(30)))!;

        var worker = CreateWorker(leaseStore, requestStore);

        var exitCode = await worker.RunAsync(new SearchReindexCommandOptions(Force: false));

        Assert.Equal(0, exitCode);
        Assert.True(worker.LastRunSummary!.SkippedDueToLease);

        var requestRepository = new InMemorySearchReindexRunRequestRepository(requestStore);
        Assert.Empty(await requestRepository.ListRecentAsync());
    }

    [Fact]
    public async Task RunAsync_force_bypasses_run_lease_when_another_holder_is_active()
    {
        var leaseStore = new SharedSearchReindexLeaseStore();
        var requestStore = new SharedSearchReindexRunRequestStore();
        var leaseService = new InMemorySearchReindexRunLeaseService(leaseStore);
        await using var held = (await leaseService.TryAcquireAsync("search-reindex", TimeSpan.FromMinutes(30)))!;

        var worker = CreateWorker(leaseStore, requestStore);

        var exitCode = await worker.RunAsync(new SearchReindexCommandOptions(Force: true));

        Assert.Equal(0, exitCode);
        Assert.False(worker.LastRunSummary!.SkippedDueToLease);
    }

    [Fact]
    public async Task RunAsync_fails_the_request_and_returns_exit_code_one_when_reindex_throws()
    {
        var leaseStore = new SharedSearchReindexLeaseStore();
        var requestStore = new SharedSearchReindexRunRequestStore();
        var worker = CreateWorker(leaseStore, requestStore, new ThrowingSearchIndexService());

        var exitCode = await worker.RunAsync(new SearchReindexCommandOptions(Force: false));

        Assert.Equal(1, exitCode);
        Assert.False(worker.LastRunSummary!.SkippedDueToLease);
        Assert.False(worker.LastRunSummary!.SkippedNoClaim);

        var requestRepository = new InMemorySearchReindexRunRequestRepository(requestStore);
        var recent = await requestRepository.ListRecentAsync();
        Assert.Single(recent);
        Assert.Equal(SearchReindexRunRequestStatus.Failed, recent[0].Status);
        Assert.NotNull(recent[0].ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_skips_when_the_queued_request_cannot_be_claimed()
    {
        var leaseStore = new SharedSearchReindexLeaseStore();
        var requestStore = new SharedSearchReindexRunRequestStore();
        var requestRepository = new InMemorySearchReindexRunRequestRepository(requestStore);
        await requestRepository.QueueAsync(new SearchReindexRunRequestCreate("other-worker"));
        await requestRepository.ClaimNextAsync("other-worker");

        var worker = CreateWorker(leaseStore, requestStore);

        var exitCode = await worker.RunAsync(new SearchReindexCommandOptions(Force: false));

        Assert.Equal(0, exitCode);
        Assert.False(worker.LastRunSummary!.SkippedDueToLease);
        Assert.True(worker.LastRunSummary!.SkippedNoClaim);
    }

    [Fact]
    public void SearchReindexCommandOptions_Parse_requires_reindex_verb()
    {
        Assert.Null(SearchReindexCommandOptions.Parse([]));
        Assert.Null(SearchReindexCommandOptions.Parse(["bogus"]));
        Assert.Null(SearchReindexCommandOptions.Parse(["reindex", "--unknown"]));
    }

    [Fact]
    public void SearchReindexCommandOptions_Parse_reads_force_flag()
    {
        var scheduled = SearchReindexCommandOptions.Parse(["reindex", "--scheduled"]);
        var forced = SearchReindexCommandOptions.Parse(["reindex", "--force"]);

        Assert.NotNull(scheduled);
        Assert.False(scheduled.Force);
        Assert.NotNull(forced);
        Assert.True(forced.Force);
    }

    private static SearchReindexScheduledWorker CreateWorker(
        SharedSearchReindexLeaseStore leaseStore,
        SharedSearchReindexRunRequestStore requestStore,
        ISearchIndexService? searchIndexService = null)
    {
        var searchReindexBuilder = new SearchReindexBuilder(searchIndexService ?? new InMemorySearchIndexService(new SharedSearchIndexStore(), new SearchIndexRevision()),
            new InMemoryNewsRepository(new SharedNewsStore(SampleNewsData.CreateSeedArticles())),
            new InMemoryForumRepository(
                SampleForumData.CreateSeedCategories(),
                SampleForumData.CreateSeedStats(),
                new InMemoryForumWriteRepository(),
                new InMemoryForumAttachmentRepository()),
            new InMemoryArticleRepository(new InMemoryArticleSubmissionRepository()),
            new InMemoryArticlesRepository(SampleArticlesData.CreateSeedArticles()),
            new CatalogSearchReindexer(searchIndexService ?? new InMemorySearchIndexService(new SharedSearchIndexStore(), new SearchIndexRevision()), new InMemoryBiographyRepository(SampleBiographyData.CreateSeedChapters()), new InMemoryDiscographyRepository(SampleDiscographyData.CreateSeedAlbums()), new InMemoryQueenHistoryRepository(SampleQueenHistoryData.CreateSeedEvents()), new InMemoryFanPerformanceRepository(SampleFanPerformanceData.CreateSeedPerformances())));

        return new SearchReindexScheduledWorker(
            searchReindexBuilder,
            new InMemorySearchReindexRunLeaseService(leaseStore),
            new InMemorySearchReindexRunRequestRepository(requestStore),
            Options.Create(new SearchReindexSchedulerOptions
            {
                UseRunLease = true,
                LeaseName = "search-reindex",
                LeaseDurationMinutes = 30,
            }),
            NullLogger<SearchReindexScheduledWorker>.Instance);
    }

    private sealed class ThrowingSearchIndexService : ISearchIndexService
    {
        public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");

        public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");

        public Task ReplaceContentTypeAsync(
            string contentType,
            IReadOnlyList<SearchDocumentEntity> documents,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");

        public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");
    }
}
