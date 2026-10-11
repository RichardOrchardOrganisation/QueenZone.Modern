using QueenZone.Data;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsAgentGuidanceProvenanceServiceTests
{
    [Fact]
    public async Task TriageCandidateAsync_records_published_guidance_revision()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedDiscoveredCandidateAsync(repository);
        var snapshot = new NewsAgentGuidanceSnapshot(9, 3, "hash-triage", "prefer member-news");
        var guided = new NewsTriageService(
            repository,
            NewsAgentTestSupport.CreateAiRunExecutor(repository, new ConfigurableNewsAiClient(
                enabled: true,
                """
                {
                  "verdict": "relevant",
                  "relevance_score": 0.93,
                  "confidence_score": 0.90,
                  "rationale": "Official Queen tour announcement.",
                  "suggested_category": "tour",
                  "entities": ["Queen", "tour"],
                  "review_notes": "Primary source."
                }
                """)),
            new NewsTriageDeterministicAnalyzer(repository),
            new FakeNewsAgentGuidanceProvider(snapshot),
            Microsoft.Extensions.Options.Options.Create(new NewsTriageOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<NewsTriageService>.Instance);

        await guided.TriageCandidateAsync(
            (await repository.GetCandidateByIdAsync(candidateId))!,
            new NewsTriageRunOptions());

        var runs = await repository.GetAiRunsForCandidateAsync(candidateId);
        Assert.Equal(9, runs[0].GuidanceRevisionId);
        Assert.Equal(3, runs[0].GuidanceRevisionNumber);
        Assert.Equal("hash-triage", runs[0].GuidanceContentHash);
        Assert.Equal("triage-v2", runs[0].PromptVersion);
    }

    [Fact]
    public async Task GenerateDraftAsync_records_published_guidance_revision()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository);
        var snapshot = new NewsAgentGuidanceSnapshot(21, 5, "hash-draft", "keep summaries short");
        var service = NewsAgentTestSupport.CreateDraftGenerationService(
            repository,
            new ConfigurableNewsAiClient(enabled: true, NewsAgentTestSupport.SampleDraftJson),
            guidanceProvider: new FakeNewsAgentGuidanceProvider(snapshot));

        await service.GenerateDraftAsync(
            (await repository.GetCandidateByIdAsync(candidateId))!,
            new NewsDraftRunOptions());

        var runs = await repository.GetAiRunsForCandidateAsync(candidateId);
        var draftRun = Assert.Single(runs, run => run.Kind == NewsAiRunKind.DraftGeneration);
        Assert.Equal(21, draftRun.GuidanceRevisionId);
        Assert.Equal(5, draftRun.GuidanceRevisionNumber);
        Assert.Equal("hash-draft", draftRun.GuidanceContentHash);
        Assert.Equal("draft-v5", draftRun.PromptVersion);
    }
}
