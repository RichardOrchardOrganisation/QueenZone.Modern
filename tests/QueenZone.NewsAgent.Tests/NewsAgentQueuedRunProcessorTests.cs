using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsAgentQueuedRunProcessorTests
{
    [Fact]
    public async Task RunOnce_completes_claimed_triage_request()
    {
        var repository = CreateRepository();
        await repository.QueueAsync(new NewsAgentRunRequestCreate("editor@example.com"));
        var executor = new FakeExecutor(new NewsAgentQueuedRunResult(
            0,
            CreateSummary(skipped: false)));
        var processor = new NewsAgentQueuedRunProcessor(
            repository,
            executor,
            CreateUrlIngestionService(),
            NullLogger<NewsAgentQueuedRunProcessor>.Instance);

        var exitCode = await processor.RunOnceAsync("news-pc");
        var request = Assert.Single(await repository.ListRecentAsync());

        Assert.Equal(0, exitCode);
        Assert.Equal(1, executor.Calls);
        Assert.Equal(NewsAgentRunRequestStatus.Completed, request.Status);
        Assert.Contains("Triage run finished", request.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunOnce_returns_request_to_pending_when_lease_is_held()
    {
        var repository = CreateRepository();
        await repository.QueueAsync(new NewsAgentRunRequestCreate("editor@example.com"));
        var processor = new NewsAgentQueuedRunProcessor(
            repository,
            new FakeExecutor(new NewsAgentQueuedRunResult(0, CreateSummary(skipped: true))),
            CreateUrlIngestionService(),
            NullLogger<NewsAgentQueuedRunProcessor>.Instance);

        var exitCode = await processor.RunOnceAsync("news-pc");
        var request = Assert.Single(await repository.ListRecentAsync());

        Assert.Equal(0, exitCode);
        Assert.Equal(NewsAgentRunRequestStatus.Pending, request.Status);
    }

    [Fact]
    public async Task RunOnce_processes_url_ingestion_without_scheduled_executor()
    {
        var repository = CreateRepository();
        await repository.QueueAsync(new NewsAgentRunRequestCreate(
            "editor@example.com",
            NewsAgentRunRequestKind.UrlIngestion,
            "https://www.queenonline.com/news/example",
            GenerateDraft: false));
        var executor = new FakeExecutor(new NewsAgentQueuedRunResult(0, CreateSummary(skipped: false)));
        var urlService = new CapturingUrlIngestionService(
            new NewsAgentUrlIngestionResult(0, "Created candidate #9 (status NeedsReview; triage-only, no draft).", 9));
        var processor = new NewsAgentQueuedRunProcessor(
            repository,
            executor,
            urlService,
            NullLogger<NewsAgentQueuedRunProcessor>.Instance);

        var exitCode = await processor.RunOnceAsync("news-pc");
        var request = Assert.Single(await repository.ListRecentAsync());

        Assert.Equal(0, exitCode);
        Assert.Equal(0, executor.Calls);
        Assert.Equal(1, urlService.Calls);
        Assert.Equal("https://www.queenonline.com/news/example", urlService.LastUrl);
        Assert.False(urlService.LastGenerateDraft);
        Assert.Equal(NewsAgentRunRequestStatus.Completed, request.Status);
        Assert.Contains("candidate #9", request.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1, 0, NewsAgentRunRequestStatus.Completed)]
    [InlineData(2, 1, NewsAgentRunRequestStatus.Failed)]
    public async Task RunOnce_persists_failure_details_even_when_run_completes(int failures, int exitCode, NewsAgentRunRequestStatus status)
    {
        var repository = CreateRepository();
        await repository.QueueAsync(new NewsAgentRunRequestCreate("editor@example.com"));
        var summary = CreateSummary(false) with
        {
            Discovery = new NewsDiscoveryRunResult(2, 0, 0, 0, 0, 0, failures, ["Queen Online: source unavailable"]),
            ExitCode = exitCode
        };
        var processor = new NewsAgentQueuedRunProcessor(repository,
            new FakeExecutor(new NewsAgentQueuedRunResult(exitCode, summary)),
            CreateUrlIngestionService(), NullLogger<NewsAgentQueuedRunProcessor>.Instance);

        Assert.Equal(exitCode, await processor.RunOnceAsync("news-pc"));
        var request = Assert.Single(await repository.ListRecentAsync());
        Assert.Equal(status, request.Status);
        Assert.Contains("Queen Online: source unavailable", request.Summary ?? request.ErrorMessage, StringComparison.Ordinal);
    }

    private static InMemoryNewsAgentRunRequestRepository CreateRepository() =>
        new(new SharedNewsAgentRunRequestStore());

    private static NewsAgentUrlIngestionService CreateUrlIngestionService() =>
        new CapturingUrlIngestionService(
            new NewsAgentUrlIngestionResult(1, "URL ingestion should not run for gathering requests."));

    private static NewsAgentRunSummary CreateSummary(bool skipped) =>
        new(
            skipped,
            AiEnabled: true,
            DryRun: false,
            Discovery: null,
            Triage: null,
            Draft: null,
            EstimatedAiSpendUsd: 0,
            ExitCode: 0);

    private sealed class FakeExecutor(NewsAgentQueuedRunResult result) : INewsAgentQueuedRunExecutor
    {
        public int Calls { get; private set; }

        public Task<NewsAgentQueuedRunResult> RunAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingUrlIngestionService : NewsAgentUrlIngestionService
    {
        private readonly NewsAgentUrlIngestionResult result;

        public CapturingUrlIngestionService(NewsAgentUrlIngestionResult result)
            : base(
                new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore()),
                new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>()),
                NewsAgentTestSupport.CreateTriageService(
                    new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore()),
                    new ConfigurableNewsAiClient(enabled: false)),
                NewsAgentTestSupport.CreateDraftGenerationService(
                    new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore()),
                    new ConfigurableNewsAiClient(enabled: false)),
                NullLogger<NewsAgentUrlIngestionService>.Instance)
        {
            this.result = result;
        }

        public int Calls { get; private set; }

        public string? LastUrl { get; private set; }

        public bool LastGenerateDraft { get; private set; }

        public override Task<NewsAgentUrlIngestionResult> IngestAsync(
            string articleUrl,
            bool generateDraft,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastUrl = articleUrl;
            LastGenerateDraft = generateDraft;
            return Task.FromResult(result);
        }
    }
}
