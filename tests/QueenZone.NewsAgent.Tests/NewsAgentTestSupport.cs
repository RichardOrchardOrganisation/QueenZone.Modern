using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

internal sealed class FakeNewsDiscoveryHttpClient(IReadOnlyDictionary<string, string> responses) : INewsDiscoveryHttpClient
{
    public Task<string> GetStringAsync(string url, CancellationToken cancellationToken = default)
    {
        if (responses.TryGetValue(url, out var body))
        {
            return Task.FromResult(body);
        }

        throw new HttpRequestException($"No fixture configured for URL '{url}'.");
    }

    public async Task<NewsDiscoveryHttpResponse> GetAsync(string url, CancellationToken cancellationToken = default)
    {
        var body = await GetStringAsync(url, cancellationToken);
        return new NewsDiscoveryHttpResponse(url, "text/html; charset=utf-8", body);
    }
}

internal sealed class ConfigurableNewsAiClient(bool enabled, string content = "{}") : INewsAiClient
{
    public bool IsEnabled { get; } = enabled;

    public Task<NewsAiChatCompletion> CompleteChatAsync(
        NewsAiChatRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new NewsAiChatCompletion(
            content,
            request.ModelRole == NewsAiModelRole.Drafting ? "openai/gpt-4.1-mini" : "openai/gpt-4.1-nano",
            1,
            1,
            0.0001m,
            false));
}

internal static class NewsAgentTestSupport
{
    public static string ReadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "NewsAgent", fileName);
        return File.ReadAllText(path);
    }

    public static NewsDiscoveryService CreateDiscoveryService(
        INewsDiscoveryRepository repository,
        INewsDiscoveryHttpClient httpClient)
    {
        INewsSourceFetcher[] fetchers =
        [
            new RssAtomSourceFetcher(httpClient),
            new SitemapSourceFetcher(httpClient),
            new AllowlistedPageSourceFetcher(httpClient)
        ];

        return new NewsDiscoveryService(
            repository,
            new NewsSourceFetcherRegistry(fetchers),
            NullLogger<NewsDiscoveryService>.Instance);
    }

    public const string SampleDraftJson = """
        {
          "title": "Queen announce 2026 tour",
          "slug": "queen-announce-2026-tour",
          "excerpt": "Queen have announced new 2026 tour dates.",
          "body": "Queen will return to the road in 2026 with dates across Europe and the UK.",
          "related_entities": ["Queen", "tour"],
          "source_urls": ["https://www.queenonline.com/news/tour-2026"],
          "source_names": ["Queen Online"],
          "attribution_text": "Source: Queen Online",
          "confidence_notes": "Primary official source.",
          "source_notes": "Official Queen Online announcement.",
          "suggested_publish_at": "2026-07-02T10:00:00Z",
          "secondary_source_warning": false
        }
        """;

    public static NewsAiRunExecutor CreateAiRunExecutor(
        INewsDiscoveryRepository repository,
        INewsAiClient aiClient) =>
        new(
            aiClient,
            repository,
            new NewsAiBudgetGuard(
                repository,
                Options.Create(new OpenRouterOptions
                {
                    ApiKey = aiClient.IsEnabled ? "test-key" : null,
                    PerRunCandidateLimit = 5,
                    PerRunBudgetUsd = 1m,
                    DailyBudgetUsd = 5m
                })),
            Options.Create(new OpenRouterOptions { ApiKey = aiClient.IsEnabled ? "test-key" : null }),
            NullLogger<NewsAiRunExecutor>.Instance);

    public static NewsTriageService CreateTriageService(
        INewsDiscoveryRepository repository,
        INewsAiClient aiClient) =>
        new(
            repository,
            CreateAiRunExecutor(repository, aiClient),
            new NewsTriageDeterministicAnalyzer(repository),
            EmptyNewsAgentGuidanceProvider.Instance,
            Options.Create(new NewsTriageOptions()),
            NullLogger<NewsTriageService>.Instance);

    public static NewsDraftGenerationService CreateDraftGenerationService(
        INewsDiscoveryRepository repository,
        INewsAiClient aiClient,
        INewsDiscoveryHttpClient? httpClient = null,
        INewsAgentGuidanceProvider? guidanceProvider = null) =>
        new(
            repository,
            CreateAiRunExecutor(repository, aiClient),
            httpClient ?? new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>()),
            guidanceProvider ?? EmptyNewsAgentGuidanceProvider.Instance,
            Options.Create(new NewsDraftGenerationOptions()),
            NullLogger<NewsDraftGenerationService>.Instance);

    public static DiscoverNewsWorker CreateDiscoverNewsWorker(
        INewsDiscoveryRepository repository,
        NewsDiscoveryService? discoveryService = null,
        INewsAiClient? triageClient = null,
        INewsAiClient? draftClient = null,
        INewsAiClient? executorClient = null,
        INewsAgentRunLeaseService? leaseService = null,
        bool dryRun = false,
        bool useRunLease = false)
    {
        triageClient ??= new ConfigurableNewsAiClient(enabled: false);
        draftClient ??= triageClient;
        executorClient ??= triageClient;

        return new DiscoverNewsWorker(
            discoveryService ?? CreateDiscoveryService(
                repository,
                new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>())),
            CreateTriageService(repository, triageClient),
            CreateDraftGenerationService(repository, draftClient),
            repository,
            new NewsAgentRunPreparation(
            CreateAiRunExecutor(repository, executorClient),
            leaseService ?? new InMemoryNewsAgentRunLeaseService(new SharedNewsAgentLeaseStore()),
            Options.Create(new OpenRouterOptions
            {
                ApiKey = triageClient.IsEnabled || draftClient.IsEnabled || executorClient.IsEnabled
                    ? "test-key"
                    : null,
                DryRun = dryRun
            }),
            Options.Create(new NewsAgentSchedulerOptions
            {
                UseRunLease = useRunLease,
                LeaseName = "discover-news"
            }),
                NullLogger<DiscoverNewsWorker>.Instance),
            NullLogger<DiscoverNewsWorker>.Instance);
    }
}
