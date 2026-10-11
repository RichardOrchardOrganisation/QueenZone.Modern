using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.NewsAgent;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsDraftGenerationServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateDraftAsync_requires_full_article_when_refresh_fails(bool storedFullArticle)
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository);
        var candidate = (await repository.GetCandidateByIdAsync(candidateId))!;
        if (storedFullArticle)
        {
            await repository.AddCandidateEvidenceAsync(candidateId, new NewsCandidateEvidenceDraft(
                candidate.SourceUrl, "Queen Online", NewsDiscoveryTrustTier.Primary,
                "Queen announce tour", null, "Full source article:\nQueen have announced tour dates.", null, DateTime.UtcNow));
        }
        var aiClient = new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson);
        var service = NewsAgentTestSupport.CreateDraftGenerationService(repository, aiClient,
            new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>()));

        if (storedFullArticle)
        {
            Assert.True((await service.GenerateDraftAsync(candidate, new NewsDraftRunOptions())).Succeeded);
            Assert.Contains("Queen have announced tour dates.", aiClient.LastRequest!.Messages[1].Content, StringComparison.Ordinal);
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.GenerateDraftAsync(candidate, new NewsDraftRunOptions()));
            Assert.Contains("full source article could not be retrieved", error.Message, StringComparison.Ordinal);
            Assert.Null(aiClient.LastRequest);
            Assert.Null(await repository.GetDraftByCandidateIdAsync(candidateId));
        }
    }

    [Fact]
    public async Task GenerateDraftAsync_rejects_metadata_only_source()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository);
        var candidate = (await repository.GetCandidateByIdAsync(candidateId))!;
        var aiClient = new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson);
        var service = NewsAgentTestSupport.CreateDraftGenerationService(repository, aiClient,
            new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
            {
                [candidate.SourceUrl] = "<meta name='description' content='The Official Queen Website'>"
            }));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateDraftAsync(candidate, new NewsDraftRunOptions()));
        Assert.Null(aiClient.LastRequest);
    }

    [Fact]
    public async Task GenerateDraftAsync_supplies_full_article_and_preserves_a_quote_beyond_4000_characters()
    {
        const string articleUrl = "https://www.queenonline.com/news/full-article";
        const string quote = "We made this together.";
        var opening = string.Join(' ', Enumerable.Repeat("Reporting about the collaboration.", 150));
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository, canonicalUrl: articleUrl);
        var content = System.Text.Json.JsonSerializer.Serialize(new
        {
            title = "Brian May collaboration",
            excerpt = "A collaboration.",
            body = $"<p>Brian May said, “{quote}”</p>",
            preserved_quotes = new[] { new { speaker = "Brian May", exact_text = quote, source_url = articleUrl } }
        });
        var aiClient = new DraftGenerationFakeAiClient(content);
        var httpClient = new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
        {
            [articleUrl] = $"<meta name='description' content='The Official Queen Website'><div class='article-detail'><p>{opening}</p><p>Brian May said, “{quote}”</p><p>The final detail.</p></div>"
        });
        var service = NewsAgentTestSupport.CreateDraftGenerationService(repository, aiClient, httpClient);

        await service.GenerateDraftAsync((await repository.GetCandidateByIdAsync(candidateId))!, new NewsDraftRunOptions());

        var prompt = aiClient.LastRequest!.Messages.Single(message => message.Role == "user").Content;
        Assert.Contains(opening, prompt, StringComparison.Ordinal);
        Assert.Contains(quote, prompt, StringComparison.Ordinal);
        Assert.Contains("The final detail.", prompt, StringComparison.Ordinal);
        var evidence = await repository.GetCandidateEvidenceAsync(candidateId);
        Assert.Contains(evidence, item => item.Excerpt?.Length > 4000 && item.Excerpt.Contains(quote, StringComparison.Ordinal));
        var draft = (await repository.GetDraftByCandidateIdAsync(candidateId))!;
        Assert.Contains($"“{quote}”", draft.ProposedBody, StringComparison.Ordinal);
        Assert.Contains(quote, draft.SourceNotes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateDraftAsync_creates_unpublished_draft_and_marks_candidate_drafted()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository);
        var service = NewsAgentTestSupport.CreateDraftGenerationService(
          repository,
          new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson));

        var result = await service.GenerateDraftAsync(
          (await repository.GetCandidateByIdAsync(candidateId))!,
          new NewsDraftRunOptions());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.DraftId);

        var draft = await repository.GetDraftByCandidateIdAsync(candidateId);
        var candidate = await repository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(draft);
        Assert.Equal("Queen announce 2026 tour", draft.ProposedTitle);
        Assert.Contains("Queen Online", draft.AttributionText, StringComparison.Ordinal);
        Assert.Equal(NewsCandidateStatus.Drafted, candidate!.Status);
        Assert.Null(candidate.PromotedNewsId);
    }

    [Fact]
    public async Task GenerateDraftAsync_refreshes_source_media_links_before_calling_ai()
    {
        const string articleUrl =
            "https://www.queenonline.com/news/i-see-you-now-roger-taylors-brand-new-single-and-video-out-now";
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            repository,
            canonicalUrl: articleUrl,
            title: "I See You Now: Roger Taylor's Brand New Single and Video");
        var aiClient = new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson);
        var httpClient = new FakeNewsDiscoveryHttpClient(new Dictionary<string, string>
        {
            [articleUrl] = """
                <html><head>
                  <title>I See You Now: Roger Taylor's Brand New Single and Video</title>
                  <meta name="description" content="Roger Taylor has released a new single and video." />
                </head><body>
                  <p>Listen to the single now @ <a href="https://rogertaylor.lnk.to/iseeyounow">Listen now</a></p>
                  <iframe src="https://www.youtube.com/embed/KZivRNcsoJw?si=test" title="YouTube video player"></iframe>
                </body></html>
                """
        });
        var service = NewsAgentTestSupport.CreateDraftGenerationService(repository, aiClient, httpClient);

        var result = await service.GenerateDraftAsync(
            (await repository.GetCandidateByIdAsync(candidateId))!,
            new NewsDraftRunOptions());

        Assert.True(result.Succeeded);
        Assert.NotNull(aiClient.LastRequest);
        Assert.Contains(
            aiClient.LastRequest.Messages,
            message => message.Content.Contains(
                "https://rogertaylor.lnk.to/iseeyounow",
                StringComparison.Ordinal));
        Assert.Contains(
            aiClient.LastRequest.Messages,
            message => message.Content.Contains(
                "https://www.youtube.com/watch?v=KZivRNcsoJw",
                StringComparison.Ordinal));

        var evidence = await repository.GetCandidateEvidenceAsync(candidateId);
        Assert.Contains(evidence, item =>
            item.Excerpt?.Contains("https://rogertaylor.lnk.to/iseeyounow", StringComparison.Ordinal) == true);

        var draft = await repository.GetDraftByCandidateIdAsync(candidateId);
        Assert.Contains(
            "<a href=\"https://rogertaylor.lnk.to/iseeyounow\">Listen to the song</a>",
            draft!.ProposedBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "<a href=\"https://www.youtube.com/watch?v=KZivRNcsoJw\">Watch the video</a>",
            draft.ProposedBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateDraftAsync_skips_when_draft_already_exists()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository);
        await repository.UpsertDraftAsync(candidateId, new NewsAgentDraftUpsert(
          "Existing draft",
          "existing-draft",
          "Existing excerpt",
          "Existing body",
          "Source: Queen Online",
          "Existing source notes",
          "Existing confidence notes",
          null,
          null));
        var service = NewsAgentTestSupport.CreateDraftGenerationService(
          repository,
          new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson));

        var result = await service.GenerateDraftAsync(
          (await repository.GetCandidateByIdAsync(candidateId))!,
          new NewsDraftRunOptions());

        Assert.False(result.Succeeded);
        Assert.NotNull(result.DraftId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateDraftAsync_force_regenerate_controls_redraft_of_drafted_candidate(bool forceRegenerate)
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository);
        var service = NewsAgentTestSupport.CreateDraftGenerationService(
          repository,
          new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson));

        await service.GenerateDraftAsync(
          (await repository.GetCandidateByIdAsync(candidateId))!,
          new NewsDraftRunOptions());

        var candidate = await repository.GetCandidateByIdAsync(candidateId);

        if (!forceRegenerate)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
              service.GenerateDraftAsync(candidate!, new NewsDraftRunOptions()));
            return;
        }

        var result = await service.GenerateDraftAsync(
          candidate!,
          new NewsDraftRunOptions(ForceRegenerate: true));

        Assert.True(result.Succeeded);
        var draft = await repository.GetDraftByCandidateIdAsync(candidateId);
        Assert.Equal("Queen announce 2026 tour", draft!.ProposedTitle);
    }

    [Fact]
    public async Task RunDraftGenerationAsync_skips_when_openrouter_disabled()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository);
        var service = NewsAgentTestSupport.CreateDraftGenerationService(
          repository,
          new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson, enabled: false));

        var result = await service.RunDraftGenerationAsync(new NewsDraftRunOptions());

        Assert.Equal(0, result.CandidatesConsidered);
        Assert.Equal(0, result.DraftsCreated);
    }

    [Fact]
    public async Task RunDraftGenerationAsync_respects_per_run_candidate_limit_and_confidence()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var baseTime = DateTime.UtcNow;
        await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            repository,
            canonicalUrl: "https://www.queenonline.com/news/low",
            title: "Low",
            discoveredAt: baseTime,
            confidenceScore: 0.10m);
        for (var i = 0; i < 3; i++)
        {
            await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
                repository,
                canonicalUrl: $"https://www.queenonline.com/news/ready-{i}",
                title: $"Ready {i}",
                discoveredAt: baseTime.AddMinutes(i + 1),
                confidenceScore: 0.90m);
        }

        var service = NewsAgentTestSupport.CreateDraftGenerationService(
            repository,
            new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson));

        var result = await service.RunDraftGenerationAsync(new NewsDraftRunOptions(PerRunCandidateLimit: 2));

        Assert.Equal(2, result.CandidatesConsidered);
        Assert.Equal(2, result.DraftsCreated);
        Assert.Equal(2, (await repository.GetCandidatesAsync(NewsCandidateStatus.NeedsReview)).Count);
    }

    [Fact]
    public async Task RunDraftGenerationAsync_returns_empty_when_no_ready_candidates()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            repository,
            confidenceScore: 0.10m);
        var service = NewsAgentTestSupport.CreateDraftGenerationService(
            repository,
            new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson));

        var result = await service.RunDraftGenerationAsync(new NewsDraftRunOptions());

        Assert.Equal(0, result.CandidatesConsidered);
        Assert.Equal(0, result.DraftsCreated);
    }

    [Fact]
    public async Task GenerateDraftAsync_rejects_candidates_outside_draftable_statuses()
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedDiscoveredCandidateAsync(repository);
        var candidate = (await repository.GetCandidateByIdAsync(candidateId))!;
        var service = NewsAgentTestSupport.CreateDraftGenerationService(
          repository,
          new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
          service.GenerateDraftAsync(candidate, new NewsDraftRunOptions()));

        Assert.Contains("needs-review or drafted", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerateDraftAsync_dry_run_controls_whether_draft_and_status_persist(bool dryRun)
    {
        var repository = new InMemoryNewsDiscoveryRepository(new SharedNewsDiscoveryStore());
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(repository);
        var service = NewsAgentTestSupport.CreateDraftGenerationService(
          repository,
          new DraftGenerationFakeAiClient(NewsAgentTestSupport.SampleDraftJson));

        var result = await service.GenerateDraftAsync(
          (await repository.GetCandidateByIdAsync(candidateId))!,
          new NewsDraftRunOptions(DryRun: dryRun));

        var draft = await repository.GetDraftByCandidateIdAsync(candidateId);
        var candidate = await repository.GetCandidateByIdAsync(candidateId);

        if (dryRun)
        {
            Assert.Null(draft);
            Assert.Equal(NewsCandidateStatus.NeedsReview, candidate!.Status);
            return;
        }

        Assert.True(result.Succeeded);
        Assert.NotNull(draft);
        Assert.Equal(NewsCandidateStatus.Drafted, candidate!.Status);
    }

    private sealed class DraftGenerationFakeAiClient(string content, bool enabled = true) : INewsAiClient
    {
        public bool IsEnabled { get; } = enabled;

        public NewsAiChatRequest? LastRequest { get; private set; }

        public Task<NewsAiChatCompletion> CompleteChatAsync(
          NewsAiChatRequest request,
          CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new NewsAiChatCompletion(
              content,
              "openai/gpt-4.1-mini",
              120,
              240,
              0.0025m,
              DryRun: false));
        }
    }
}
