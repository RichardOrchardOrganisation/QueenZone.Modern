using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsAiRunExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_records_successful_ai_run()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await CreateCandidateAsync(repository);
        var executor = CreateExecutor(
            repository,
            new FakeNewsAiClient(enabled: true, completion: new NewsAiChatCompletion(
                """{"relevant":true}""",
                "openai/gpt-4.1-nano",
                100,
                20,
                0.0002m,
                DryRun: false)));

        executor.BeginRun();
        var result = await executor.ExecuteAsync(
            candidateId,
            NewsAiRunKind.Triage,
            new NewsAiChatRequest(NewsAiModelRole.Triage, "triage-v1", [new NewsAiChatMessage("user", "Classify headline.")]));

        Assert.Equal("""{"relevant":true}""", result.Completion.Content);
        var runs = await repository.GetAiRunsForCandidateAsync(candidateId);
        Assert.Single(runs);
        Assert.Equal(NewsAiRunStatus.Succeeded, runs[0].Status);
        Assert.Equal(100, runs[0].InputTokens);
        Assert.Equal(0.0002m, runs[0].EstimatedCostUsd);
    }

    [Fact]
    public async Task ExecuteAsync_marks_run_failed_when_client_throws()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await CreateCandidateAsync(repository);
        var executor = CreateExecutor(
            repository,
            new FakeNewsAiClient(enabled: true, exception: new HttpRequestException("Provider unavailable")));

        executor.BeginRun();

        await Assert.ThrowsAsync<HttpRequestException>(() => executor.ExecuteAsync(
            candidateId,
            NewsAiRunKind.Triage,
            new NewsAiChatRequest(NewsAiModelRole.Triage, "triage-v1", [new NewsAiChatMessage("user", "Classify headline.")])));

        var runs = await repository.GetAiRunsForCandidateAsync(candidateId);
        Assert.Single(runs);
        Assert.Equal(NewsAiRunStatus.Failed, runs[0].Status);
        Assert.Contains("Provider unavailable", runs[0].ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_ai_disabled()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await CreateCandidateAsync(repository);
        var executor = CreateExecutor(repository, new FakeNewsAiClient(enabled: false));

        executor.BeginRun();

        await Assert.ThrowsAsync<NewsAiDisabledException>(() => executor.ExecuteAsync(
            candidateId,
            NewsAiRunKind.Triage,
            new NewsAiChatRequest(NewsAiModelRole.Triage, "triage-v1", [new NewsAiChatMessage("user", "Classify headline.")])));
    }

    [Fact]
    public async Task ExecuteAsync_records_cost_before_stopping_future_calls_over_budget()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await CreateCandidateAsync(repository);
        var aiClient = new FakeNewsAiClient(enabled: true, completion: new NewsAiChatCompletion(
            """{"relevant":true}""",
            "openai/gpt-4.1-nano",
            100,
            20,
            0.02m,
            DryRun: false));
        var executor = CreateExecutor(repository, aiClient, perRunBudget: 0.01m);

        executor.BeginRun();

        await Assert.ThrowsAsync<NewsAiBudgetExceededException>(() => executor.ExecuteAsync(
            candidateId,
            NewsAiRunKind.Triage,
            new NewsAiChatRequest(NewsAiModelRole.Triage, "triage-v1", [new NewsAiChatMessage("user", "Classify headline.")])));

        var runs = await repository.GetAiRunsForCandidateAsync(candidateId);
        Assert.Single(runs);
        Assert.Equal(NewsAiRunStatus.Succeeded, runs[0].Status);
        Assert.Equal(0.02m, runs[0].EstimatedCostUsd);

        await Assert.ThrowsAsync<NewsAiBudgetExceededException>(() => executor.ExecuteAsync(
            candidateId,
            NewsAiRunKind.Triage,
            new NewsAiChatRequest(NewsAiModelRole.Triage, "triage-v1", [new NewsAiChatMessage("user", "Classify headline again.")])));
        Assert.Equal(1, aiClient.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_records_guidance_revision_without_storing_guidance_text()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await CreateCandidateAsync(repository);
        var executor = CreateExecutor(
            repository,
            new FakeNewsAiClient(enabled: true, completion: new NewsAiChatCompletion(
                """{"relevant":true}""",
                "openai/gpt-4.1-nano",
                100,
                20,
                0.0002m,
                DryRun: false)));

        executor.BeginRun();
        await executor.ExecuteAsync(
            candidateId,
            NewsAiRunKind.Triage,
            new NewsAiChatRequest(NewsAiModelRole.Triage, "triage-v2", [new NewsAiChatMessage("user", "Classify headline.")]),
            guidance: new NewsAgentGuidanceSnapshot(12, 4, "abc123", "prefer member-news"));

        var runs = await repository.GetAiRunsForCandidateAsync(candidateId);
        Assert.Equal(12, runs[0].GuidanceRevisionId);
        Assert.Equal(4, runs[0].GuidanceRevisionNumber);
        Assert.Equal("abc123", runs[0].GuidanceContentHash);
        Assert.Equal("triage-v2", runs[0].PromptVersion);
        Assert.DoesNotContain("prefer member-news", runs[0].StructuredResultJson, StringComparison.Ordinal);
    }

    private static NewsAiRunExecutor CreateExecutor(
        INewsDiscoveryRepository repository,
        INewsAiClient aiClient,
        decimal perRunBudget = 1m,
        decimal dailyBudget = 5m) =>
        new(
            aiClient,
            repository,
            new NewsAiBudgetGuard(repository, Options.Create(new OpenRouterOptions
            {
                ApiKey = aiClient.IsEnabled ? "test-key" : null,
                PerRunCandidateLimit = 5,
                PerRunBudgetUsd = perRunBudget,
                DailyBudgetUsd = dailyBudget
            })),
            Options.Create(new OpenRouterOptions
            {
                ApiKey = aiClient.IsEnabled ? "test-key" : null
            }),
            NullLogger<NewsAiRunExecutor>.Instance);

    private static async Task<int> CreateCandidateAsync(INewsDiscoveryRepository repository)
    {
        var sourceId = await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "queen-online",
            "Queen Online",
            "https://www.queenonline.com/",
            "https://www.queenonline.com/feed/",
            NewsDiscoverySourceType.Rss,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));

        return await repository.CreateCandidateAsync(new NewsCandidateCreateRequest(
            sourceId,
            "https://www.queenonline.com/news/test",
            "Queen test headline",
            DateTime.UtcNow,
            "Excerpt",
            DateTime.UtcNow));
    }

    private sealed class FakeNewsAiClient(
        bool enabled,
        NewsAiChatCompletion? completion = null,
        Exception? exception = null) : INewsAiClient
    {
        public bool IsEnabled { get; } = enabled;

        public int CallCount { get; private set; }

        public Task<NewsAiChatCompletion> CompleteChatAsync(
            NewsAiChatRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(completion!);
        }
    }
}
