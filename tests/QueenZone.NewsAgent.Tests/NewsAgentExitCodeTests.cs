using System.Reflection;
using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsAgentExitCodeTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public async Task Discovery_records_failures_and_only_fails_when_all_sources_fail(bool includeSuccess, int expected)
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        await SeedSource(repository, "broken");
        if (includeSuccess)
        {
            await SeedSource(repository, "healthy");
        }
        var logger = new CaptureLogger<NewsDiscoveryService>();
        var worker = NewsAgentTestSupport.CreateDiscoverNewsWorker(repository, CreateDiscovery(repository, logger));

        Assert.Equal(expected, await worker.RunAsync(Options()));

        var summary = Assert.IsType<NewsAgentRunSummary>(worker.LastRunSummary);
        Assert.Equal(1, summary.TotalFailures);
        Assert.Contains("broken name (broken)", Assert.Single(summary.Errors), StringComparison.Ordinal);
        Assert.Contains("source unavailable", summary.Errors[0], StringComparison.Ordinal);
        Assert.Contains(logger.Events, e => e.Level == LogLevel.Warning);
        var telemetry = new CaptureLogger<DiscoverNewsWorker>();
        NewsAgentRunTelemetry.LogRunCompleted(telemetry, summary);
        Assert.Contains(telemetry.Events, e => e.Message.Contains(summary.Errors[0], StringComparison.Ordinal));
        Assert.Contains(summary.Errors[0], NewsAgentQueuedRunProcessor.FormatSummary(summary, expected), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 0)]
    public async Task Item_failures_use_same_rule_for_triage_and_drafting(bool draft, bool includeSuccess, int expected)
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        for (var index = 0; index < (includeSuccess ? 2 : 1); index++)
        {
            var url = $"https://www.queenonline.com/news/item-{index}";
            if (draft)
            {
                await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository, canonicalUrl: url, title: $"Queen concert {index}", excerpt: $"Official announcement {index}");
            }
            else
            {
                await NewsDiscoveryTestSeeder.SeedDiscoveredCandidateAsync(repository, canonicalUrl: url, title: $"Queen concert {index}", excerpt: $"Official announcement {index}");
            }
        }
        var client = new MixedAiClient(draft);
        var worker = NewsAgentTestSupport.CreateDiscoverNewsWorker(repository, triageClient: client, draftClient: client);

        Assert.Equal(expected, await worker.RunAsync(Options() with { Triage = !draft, TriageOnly = !draft, Draft = draft, DraftOnly = draft }));
        Assert.Equal(1, worker.LastRunSummary!.TotalFailures);
        Assert.Contains("Candidate", Assert.Single(worker.LastRunSummary.Errors), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("GetSourcesAsync", 1)]
    [InlineData("GetCandidatesAsync", 1)]
    [InlineData("GetCandidatesAsync", 2)]
    public async Task Step_exception_is_recorded_and_later_steps_still_run(string failingMethod, int failureCall)
    {
        var inner = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var repository = DispatchProxy.Create<INewsDiscoveryRepository, ThrowingRepository>();
        var proxy = (ThrowingRepository)repository;
        proxy.Inner = inner;
        proxy.FailingMethod = failingMethod;
        proxy.FailureCall = failureCall;
        var worker = NewsAgentTestSupport.CreateDiscoverNewsWorker(repository, triageClient: new ConfigurableNewsAiClient(true));

        Assert.Equal(1, await worker.RunAsync(Options() with { Triage = true, Draft = true }));
        Assert.Contains("step unavailable", Assert.Single(worker.LastRunSummary!.StepErrors), StringComparison.Ordinal);
        if (failureCall == 1)
        {
            Assert.NotNull(worker.LastRunSummary.Draft);
            Assert.Equal(0, worker.LastRunSummary.Draft.Failures);
        }
    }

    [Fact]
    public async Task Third_failure_emits_distinct_error_and_success_resets_persisted_count()
    {
        var store = new SharedNewsDiscoveryStore();
        var repository = new InMemoryNewsDiscoveryRepository(store);
        var id = await SeedSource(repository, "broken");
        var logger = new CaptureLogger<NewsDiscoveryService>();
        for (var run = 1; run <= 3; run++)
        {
            // Recreating service and repository proves the streak belongs to the store.
            var service = CreateDiscovery(new InMemoryNewsDiscoveryRepository(store), logger);
            await service.RunFetchAsync(new NewsDiscoveryRunOptions(Force: true));
            Assert.Equal(run, store.GetSourceById(id)!.ConsecutiveFailureCount);
            Assert.Equal(run == 3 ? 1 : 0, logger.Events.Count(e => e.Level == LogLevel.Error));
        }
        Assert.Equal(NewsDiscoveryService.SourceFailureThresholdReached, Assert.Single(logger.Events, e => e.Level == LogLevel.Error).Id);
        await CreateDiscovery(repository, logger, allHealthy: true).RunFetchAsync(new NewsDiscoveryRunOptions(Force: true));
        Assert.Equal(0, store.GetSourceById(id)!.ConsecutiveFailureCount);
        await CreateDiscovery(repository, logger).RunFetchAsync(new NewsDiscoveryRunOptions(Force: true));
        Assert.Equal(1, store.GetSourceById(id)!.ConsecutiveFailureCount);
        Assert.Single(logger.Events, e => e.Level == LogLevel.Error);
        await CreateDiscovery(repository, logger).RunFetchAsync(new NewsDiscoveryRunOptions(Force: true, DryRun: true));
        Assert.Equal(1, store.GetSourceById(id)!.ConsecutiveFailureCount);
    }

    [Fact]
    public async Task Empty_run_succeeds_and_disabled_drafting_fails()
    {
        var worker = NewsAgentTestSupport.CreateDiscoverNewsWorker(new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore()));
        Assert.Equal(0, await worker.RunAsync(Options() with { Triage = true }));
        Assert.Equal(1, await worker.RunAsync(Options() with { Draft = true, DraftOnly = true }));
        Assert.Null(worker.LastRunSummary!.Discovery);
        Assert.Null(worker.LastRunSummary.Triage);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task Existing_drafts_are_not_counted_as_attempts(bool includeFailure, int expected)
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var id = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository);
        await repository.UpsertDraftAsync(id, new NewsAgentDraftUpsert("Queen", null, "Excerpt", "Body", null, null, null, null, null));
        if (includeFailure)
        {
            await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository,
                canonicalUrl: "https://www.queenonline.com/news/another", title: "Queen studio news", excerpt: "New album announcement");
        }
        var worker = NewsAgentTestSupport.CreateDiscoverNewsWorker(repository,
            draftClient: new ConfigurableNewsAiClient(true, "{invalid-json"));
        Assert.Equal(expected, await worker.RunAsync(Options() with { Force = false, Draft = true, DraftOnly = true }));
        Assert.Equal(1, worker.LastRunSummary!.Draft!.Skipped);
        Assert.Equal(includeFailure ? 1 : 0, worker.LastRunSummary.Draft.CandidatesAttempted);
    }

    [Fact]
    public async Task Source_timeout_is_a_recorded_failure()
    {
        var store = new SharedNewsDiscoveryStore();
        var repository = new InMemoryNewsDiscoveryRepository(store);
        var id = await SeedSource(repository, "broken");
        var service = new NewsDiscoveryService(repository, new NewsSourceFetcherRegistry([new TimeoutFetcher()]),
            new CaptureLogger<NewsDiscoveryService>());
        var worker = NewsAgentTestSupport.CreateDiscoverNewsWorker(repository, service);
        Assert.Equal(1, await worker.RunAsync(Options()));
        Assert.Contains("timed out", Assert.Single(worker.LastRunSummary!.Errors), StringComparison.Ordinal);
        Assert.Equal(1, store.GetSourceById(id)!.ConsecutiveFailureCount);
    }

    private sealed class TimeoutFetcher : INewsSourceFetcher
    {
        public NewsDiscoverySourceType SourceType => NewsDiscoverySourceType.Rss;
        public Task<IReadOnlyList<FetchedNewsItem>> FetchAsync(NewsDiscoverySource source, CancellationToken cancellationToken = default) =>
            throw new TaskCanceledException("Source timed out");
    }

    private static DiscoverNewsCommandOptions Options() => new(false, false, true, false, false, false, false);

    private static Task<int> SeedSource(INewsDiscoveryRepository repository, string key) => repository.UpsertSourceAsync(
        new NewsDiscoverySourceDraft(key, key + " name", "https://example.com", "https://example.com/feed", NewsDiscoverySourceType.Rss, NewsDiscoveryTrustTier.Primary, 60, true, null));

    private static NewsDiscoveryService CreateDiscovery(INewsDiscoveryRepository repository, ILogger<NewsDiscoveryService> logger, bool allHealthy = false) =>
        new(repository, new NewsSourceFetcherRegistry([new Fetcher(allHealthy)]), logger);

    private sealed class Fetcher(bool allHealthy) : INewsSourceFetcher
    {
        public NewsDiscoverySourceType SourceType => NewsDiscoverySourceType.Rss;
        public Task<IReadOnlyList<FetchedNewsItem>> FetchAsync(NewsDiscoverySource source, CancellationToken cancellationToken = default) =>
            allHealthy || source.Key == "healthy" ? Task.FromResult<IReadOnlyList<FetchedNewsItem>>([]) : throw new HttpRequestException("source unavailable");
    }

    private sealed class MixedAiClient(bool draft) : INewsAiClient
    {
        private int calls;
        public bool IsEnabled => true;
        public Task<NewsAiChatCompletion> CompleteChatAsync(NewsAiChatRequest request, CancellationToken cancellationToken = default)
        {
            var content = calls++ == 0 ? "{invalid-json" : draft ? NewsAgentTestSupport.SampleDraftJson : """
                {"verdict":"relevant","relevance_score":0.95,"confidence_score":0.95,"rationale":"Official Queen news","suggested_category":"tour","entities":["Queen"],"review_notes":"Primary source"}
                """;
            return Task.FromResult(new NewsAiChatCompletion(content, "test", 1, 1, 0, false));
        }
    }

    public class ThrowingRepository : DispatchProxy
    {
        public INewsDiscoveryRepository Inner { get; set; } = null!;
        public string FailingMethod { get; set; } = string.Empty;
        public int FailureCall { get; set; }
        private int calls;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == FailingMethod && ++calls == FailureCall)
            {
                throw new InvalidOperationException("step unavailable");
            }
            return targetMethod.Invoke(Inner, args);
        }
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId Id, string Message)> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Events.Add((logLevel, eventId, formatter(state, exception)));
    }
}
