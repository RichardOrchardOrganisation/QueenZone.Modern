using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.NewsAgent;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin.NewsDiscovery;

namespace QueenZone.Web.Tests;

public sealed partial class AdminNewsDiscoveryRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string AdminEmail = "admin@test.local";

    private const string SampleDraftJson = """
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
    private readonly WebApplicationFactory<Program> factory;

    public AdminNewsDiscoveryRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AnonymousUserCannotAccessDiscoveryReviewQueue()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/admin/news-discovery");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedNonAdminCannotAccessDiscoveryReviewQueue()
    {
        var client = CreateClient("stranger@example.com");

        var response = await client.GetAsync("/admin/news-discovery");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminCanReviewRejectAndPromoteWithoutPublishingPublicly()
    {
        var newsStore = new SharedNewsStore();
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, newsStore, discoveryStore);

        var queueBody = await client.GetStringAsync("/admin/news-discovery?status=Drafted");
        Assert.Contains("Discovery review candidate", queueBody);
        Assert.Contains("/admin/news-discovery/" + candidateId, queueBody);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Discovery review candidate", reviewBody);
        Assert.Contains("https://www.queenonline.com/news/review-candidate", reviewBody);
        Assert.Contains("Mentions Queen tour dates.", reviewBody);
        Assert.Contains("Discovery draft title", reviewBody);

        var publicBeforePromote = await client.GetStringAsync("/news");
        Assert.DoesNotContain("Discovery draft title", publicBeforePromote);

        var promoteResponse = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, promoteResponse.StatusCode);
        var editPath = promoteResponse.Headers.Location!.OriginalString;
        Assert.Matches("/admin/news/\\d+/edit", editPath);

        var articleId = int.Parse(editPath.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);
        var editBody = await client.GetStringAsync(editPath);
        Assert.Contains("Discovery provenance", editBody);
        Assert.Contains($"/admin/news-discovery/{candidateId}", editBody);

        var publicAfterPromote = await client.GetStringAsync("/news");
        Assert.DoesNotContain("Discovery draft title", publicAfterPromote);

        var previewBody = await client.GetStringAsync($"/admin/news/{articleId}/preview");
        Assert.Contains("Discovery draft title", previewBody);
        Assert.Contains("This draft is hidden from the public archive.", previewBody);

        var promotedCandidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(promotedCandidate);
        Assert.Equal(NewsCandidateStatus.PromotedToArticle, promotedCandidate.Status);
        Assert.Equal(articleId, promotedCandidate.PromotedNewsId);
    }

    [Fact]
    public async Task DeletePromotedArticle_clears_discovery_link()
    {
        var newsStore = new SharedNewsStore();
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, newsStore, discoveryStore);

        var promoteResponse = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        var articleId = int.Parse(promoteResponse.Headers.Location!.OriginalString.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);

        var deleteResponse = await PostAdminNewsActionAsync(client, $"/admin/news/{articleId}/delete");
        Assert.Equal(HttpStatusCode.Redirect, deleteResponse.StatusCode);

        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(candidate);
        Assert.Null(candidate.PromotedNewsId);
    }

    [Fact]
    public async Task AuthorizedAdminCanRegenerateDraftFromReviewPage()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var initialReviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Regenerate draft with AI", initialReviewBody);
        Assert.Contains("data-busy-submit", initialReviewBody);
        Assert.Contains("data-busy-label=\"Regenerating draft\"", initialReviewBody);
        Assert.Contains("data-busy-timer=\"true\"", initialReviewBody);
        Assert.Contains("data-busy-status", initialReviewBody);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/regeneratedraft", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Draft regenerated successfully", reviewBody);
        Assert.Contains("Queen announce 2026 tour", reviewBody);

        var draft = await discoveryRepository.GetDraftByCandidateIdAsync(candidateId);
        Assert.NotNull(draft);
        Assert.Equal("Queen announce 2026 tour", draft.ProposedTitle);
    }

    [Fact]
    public async Task AuthorizedAdminCanGenerateDraftForDiscoveredCandidateFromReviewPage()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDiscoveredCandidateAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var initialReviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Generate draft with AI", initialReviewBody);
        Assert.DoesNotContain("--draft-only", initialReviewBody);
        Assert.Contains("data-busy-label=\"Generating draft\"", initialReviewBody);
        Assert.Contains("data-busy-timer=\"true\"", initialReviewBody);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/regeneratedraft", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Draft generated successfully", reviewBody);
        Assert.Contains("Queen announce 2026 tour", reviewBody);

        var draft = await discoveryRepository.GetDraftByCandidateIdAsync(candidateId);
        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(draft);
        Assert.Equal("Queen announce 2026 tour", draft.ProposedTitle);
        Assert.Equal(NewsCandidateStatus.Drafted, candidate!.Status);
    }

    [Fact]
    public async Task RegenerateDraft_reports_error_when_openrouter_is_not_configured()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        var client = CreateClient(
            AdminEmail,
            new SharedNewsStore(),
            discoveryStore,
            openRouterApiKey: null,
            aiClientEnabled: false);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/regeneratedraft", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Draft generation requires OpenRouter configuration", reviewBody);
    }

    [Fact]
    public async Task AuthorizedAdminCanGenerateDraftForRejectedCandidateFromReviewPage()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://www.queenonline.com/news/reject-candidate",
            title: "Reject me",
            excerpt: "Excerpt",
            sourceFeedOrSiteUrl: null,
            relevanceScore: null,
            confidenceScore: null);
        await discoveryRepository.TryUpdateCandidateStatusAsync(
            candidateId,
            new NewsCandidateStatusUpdate(NewsCandidateStatus.Rejected));
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var initialReviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Generate draft with AI", initialReviewBody);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/regeneratedraft", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Draft generated successfully", reviewBody);
        Assert.Contains("Queen announce 2026 tour", reviewBody);

        var draft = await discoveryRepository.GetDraftByCandidateIdAsync(candidateId);
        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(draft);
        Assert.Equal("Queen announce 2026 tour", draft.ProposedTitle);
        Assert.Equal(NewsCandidateStatus.Drafted, candidate!.Status);
    }

    [Fact]
    public async Task RegenerateDraft_reports_failure_when_ai_returns_invalid_json()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        var client = CreateClient(
            AdminEmail,
            new SharedNewsStore(),
            discoveryStore,
            draftResponseJson: "{not-json");

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/regeneratedraft", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Draft regeneration failed", reviewBody);
    }

    [Fact]
    public async Task RegenerateDraft_reports_when_ai_returns_no_structured_content()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        var client = CreateClient(
            AdminEmail,
            new SharedNewsStore(),
            discoveryStore,
            draftResponseJson: string.Empty);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/regeneratedraft", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Draft regeneration did not produce a new draft", reviewBody);
    }

    [Fact]
    public async Task RegenerateDraft_returns_not_found_for_missing_candidate()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);
        var reviewPage = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        var token = ExtractAntiforgeryToken(reviewPage);

        var response = await client.PostAsync(
            "/admin/news-discovery/99999/regeneratedraft",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [AdminNewsDiscoveryPageModel.AntiforgeryTokenFieldName] = token
            }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectActionWithoutAntiforgeryTokenReturnsBadRequest()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://www.queenonline.com/news/reject-candidate",
            title: "Reject me",
            excerpt: "Excerpt",
            sourceFeedOrSiteUrl: null,
            relevanceScore: null,
            confidenceScore: null);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var response = await client.PostAsync(
            $"/admin/news-discovery/{candidateId}/reject",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminCanRejectCandidate()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://www.queenonline.com/news/reject-candidate",
            title: "Reject me",
            excerpt: "Excerpt",
            sourceFeedOrSiteUrl: null,
            relevanceScore: null,
            confidenceScore: null);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var rejectResponse = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/reject", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, rejectResponse.StatusCode);

        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(candidate);
        Assert.Equal(NewsCandidateStatus.Rejected, candidate.Status);
    }

    [Fact]
    public async Task AuthorizedAdminCanIgnoreDuplicateCandidate()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var (firstId, secondId) = await NewsDiscoveryTestSeeder.SeedDuplicatePairAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var ignoreResponse = await PostActionAsync(client, $"/admin/news-discovery/{secondId}/ignoreduplicate", secondId);
        Assert.Equal(HttpStatusCode.Redirect, ignoreResponse.StatusCode);

        var ignored = await discoveryRepository.GetCandidateByIdAsync(secondId);
        Assert.NotNull(ignored);
        Assert.Equal(NewsCandidateStatus.IgnoredDuplicate, ignored.Status);
        Assert.Equal(firstId, ignored.DuplicateOfCandidateId);
    }

    [Fact]
    public async Task AuthorizedAdminCanPromoteNeedsReviewCandidateWithAttribution()
    {
        var newsStore = new SharedNewsStore();
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateWithDraftAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, newsStore, discoveryStore);

        var promoteResponse = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, promoteResponse.StatusCode);

        var editPath = promoteResponse.Headers.Location!.OriginalString;
        var articleId = int.Parse(editPath.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);
        var previewBody = await client.GetStringAsync($"/admin/news/{articleId}/preview");
        Assert.Contains("Needs-review draft title", previewBody);
        Assert.Contains("Source: Queen Online", previewBody);

        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(candidate);
        Assert.Equal(NewsCandidateStatus.PromotedToArticle, candidate.Status);
    }

    [Fact]
    public async Task Promote_with_overlong_title_shows_validation_error()
    {
        var newsStore = new SharedNewsStore();
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        await discoveryRepository.UpsertDraftAsync(candidateId, new NewsAgentDraftUpsert(
            new string('x', NewsValidation.MaxTitleLength + 1),
            "overlong-title",
            "Draft excerpt for review queue.",
            "Draft body for review queue.",
            null,
            null,
            null,
            DateTime.UtcNow.Date,
            null));
        var client = CreateClient(AdminEmail, newsStore, discoveryStore);

        var promoteResponse = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, promoteResponse.StatusCode);
        Assert.Equal($"/admin/news-discovery/{candidateId}", promoteResponse.Headers.Location!.OriginalString);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains($"Title must be {NewsValidation.MaxTitleLength} characters or fewer.", reviewBody);

        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(candidate);
        Assert.Equal(NewsCandidateStatus.Drafted, candidate.Status);
        Assert.Null(candidate.PromotedNewsId);
    }

    [Fact]
    public async Task Promote_with_overlong_excerpt_shows_validation_error()
    {
        var newsStore = new SharedNewsStore();
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        await discoveryRepository.UpsertDraftAsync(candidateId, new NewsAgentDraftUpsert(
            "Draft title",
            "draft-title",
            new string('x', NewsValidation.MaxExcerptLength + 1),
            "Draft body for review queue.",
            null,
            null,
            null,
            DateTime.UtcNow.Date,
            null));
        var client = CreateClient(AdminEmail, newsStore, discoveryStore);

        var promoteResponse = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, promoteResponse.StatusCode);
        Assert.Equal($"/admin/news-discovery/{candidateId}", promoteResponse.Headers.Location!.OriginalString);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains($"Excerpt must be {NewsValidation.MaxExcerptLength} characters or fewer.", reviewBody);

        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(candidate);
        Assert.Equal(NewsCandidateStatus.Drafted, candidate.Status);
        Assert.Null(candidate.PromotedNewsId);
    }

    [Fact]
    public async Task Promote_with_overlong_source_url_shows_validation_error()
    {
        var newsStore = new SharedNewsStore();
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var longUrl = "https://www.queenonline.com/news/" + new string('a', NewsValidation.MaxSourceUrlLength);
        var sourceId = await discoveryRepository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "long-url-source",
            "Long URL Source",
            "https://example.com/",
            null,
            NewsDiscoverySourceType.Rss,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));
        var discoveredAt = DateTime.UtcNow;
        var candidateId = await discoveryRepository.CreateCandidateAsync(new NewsCandidateCreateRequest(
            sourceId,
            longUrl,
            "Long URL candidate",
            discoveredAt,
            "Excerpt",
            discoveredAt));
        await discoveryRepository.TryUpdateCandidateStatusAsync(
            candidateId,
            new NewsCandidateStatusUpdate(
                NewsCandidateStatus.NeedsReview,
                RelevanceScore: 0.9m,
                ConfidenceScore: 0.8m));
        await discoveryRepository.TryUpdateCandidateStatusAsync(
            candidateId,
            new NewsCandidateStatusUpdate(NewsCandidateStatus.Drafted));
        await discoveryRepository.UpsertDraftAsync(candidateId, new NewsAgentDraftUpsert(
            "Valid title",
            "valid-title",
            "Draft excerpt",
            "Draft body",
            null,
            null,
            null,
            discoveredAt.Date,
            null));

        var client = CreateClient(AdminEmail, newsStore, discoveryStore);
        var promoteResponse = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, promoteResponse.StatusCode);
        Assert.Equal($"/admin/news-discovery/{candidateId}", promoteResponse.Headers.Location!.OriginalString);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains($"Source URL must be {NewsValidation.MaxSourceUrlLength} characters or fewer.", reviewBody);
    }

    [Fact]
    public async Task Promote_with_overlong_ai_slug_caps_slug_before_creating_draft()
    {
        var newsStore = new SharedNewsStore();
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        await discoveryRepository.UpsertDraftAsync(candidateId, new NewsAgentDraftUpsert(
            "Draft title with verbose generated slug",
            new string('a', NewsSlug.MaxLength + 50),
            "Draft excerpt for review queue.",
            "Draft body for review queue.",
            null,
            null,
            null,
            DateTime.UtcNow.Date,
            null));
        var client = CreateClient(AdminEmail, newsStore, discoveryStore);

        var promoteResponse = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);

        Assert.Equal(HttpStatusCode.Redirect, promoteResponse.StatusCode);
        var editPath = promoteResponse.Headers.Location!.OriginalString;
        Assert.Matches("/admin/news/\\d+/edit", editPath);

        var articleId = int.Parse(editPath.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);
        var article = newsStore.GetArticle(articleId);
        Assert.NotNull(article);
        Assert.Equal(NewsSlug.MaxLength, article.Slug!.Length);
    }

    [Fact]
    public async Task AuthorizedAdminCanEditDraftAndMoveCandidateToDrafted()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateWithDraftAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var editPage = await client.GetStringAsync($"/admin/news-discovery/{candidateId}/edit-draft");
        Assert.Contains("Needs-review draft title", editPage);
        Assert.Contains($"action=\"/admin/news-discovery/{candidateId}/edit-draft\"", editPage);
        Assert.Contains("qz-rte", editPage);
        Assert.Contains("name=\"form.body\"", editPage);
        Assert.Contains("/js/editor/rich-text-editor.js", editPage);
        Assert.Contains("data-af-token", editPage);

        var saveResponse = await PostDraftEditAsync(
            client,
            candidateId,
            new Dictionary<string, string>
            {
                ["form.title"] = "Edited discovery draft",
                ["form.slug"] = "edited-discovery-draft",
                ["form.excerpt"] = "Edited excerpt.",
                ["form.body"] = "Edited body text.",
                ["form.attributionText"] = "Edited attribution.",
                ["form.sourceNotes"] = "Edited source notes.",
                ["form.confidenceNotes"] = "Edited confidence notes.",
                ["form.suggestedPublishAt"] = "2026-07-15"
            });
        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);
        Assert.Equal($"/admin/news-discovery/{candidateId}", saveResponse.Headers.Location!.OriginalString);

        var draft = await discoveryRepository.GetDraftByCandidateIdAsync(candidateId);
        Assert.NotNull(draft);
        Assert.Equal("Edited discovery draft", draft.ProposedTitle);
        Assert.Equal("Edited body text.", draft.ProposedBody);

        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(candidate);
        Assert.Equal(NewsCandidateStatus.Drafted, candidate.Status);
    }

    [Fact]
    public async Task EditDraftSanitizesRichTextBodyBeforeSaving()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateWithDraftAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var saveResponse = await PostDraftEditAsync(
            client,
            candidateId,
            new Dictionary<string, string>
            {
                ["form.title"] = "Rich discovery draft",
                ["form.slug"] = "rich-discovery-draft",
                ["form.excerpt"] = "Edited excerpt.",
                ["form.body"] = """<p><strong>Edited</strong> body.</p><script>alert("x")</script>""",
                ["form.attributionText"] = "",
                ["form.sourceNotes"] = "",
                ["form.confidenceNotes"] = "",
                ["form.suggestedPublishAt"] = "2026-07-15"
            });

        Assert.Equal(HttpStatusCode.Redirect, saveResponse.StatusCode);

        var draft = await discoveryRepository.GetDraftByCandidateIdAsync(candidateId);
        Assert.NotNull(draft);
        Assert.Contains("<strong>Edited</strong>", draft.ProposedBody);
        Assert.DoesNotContain("<script", draft.ProposedBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditDraftValidationFailuresAreReturnedForInvalidForm()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateWithDraftAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var response = await PostDraftEditAsync(
            client,
            candidateId,
            new Dictionary<string, string>
            {
                ["form.title"] = "",
                ["form.excerpt"] = "",
                ["form.body"] = ""
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Title is required.", body);
        Assert.Contains("Excerpt is required.", body);
        Assert.Contains("Body is required.", body);
    }

    [Fact]
    public async Task ReviewAndEditDraftReturnNotFoundForMissingCandidate()
    {
        var client = CreateClient(AdminEmail);

        var reviewResponse = await client.GetAsync("/admin/news-discovery/99999");
        Assert.Equal(HttpStatusCode.NotFound, reviewResponse.StatusCode);

        var editResponse = await client.GetAsync("/admin/news-discovery/99999/edit-draft");
        Assert.Equal(HttpStatusCode.NotFound, editResponse.StatusCode);
    }

    [Fact]
    public async Task RejectActionReturnsNotFoundForMissingCandidate()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://www.queenonline.com/news/reject-candidate",
            title: "Reject me",
            excerpt: "Excerpt",
            sourceFeedOrSiteUrl: null,
            relevanceScore: null,
            confidenceScore: null);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);
        var reviewPage = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        var token = ExtractAntiforgeryToken(reviewPage);

        var response = await client.PostAsync(
            "/admin/news-discovery/99999/reject",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                [AdminNewsDiscoveryPageModel.AntiforgeryTokenFieldName] = token
            }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RejectActionRedirectsWhenCandidateCannotBeRejectedAgain()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://www.queenonline.com/news/reject-candidate",
            title: "Reject me",
            excerpt: "Excerpt",
            sourceFeedOrSiteUrl: null,
            relevanceScore: null,
            confidenceScore: null);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/reject", candidateId);
        var secondReject = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/reject", candidateId);

        Assert.Equal(HttpStatusCode.Redirect, secondReject.StatusCode);
        Assert.Equal($"/admin/news-discovery/{candidateId}", secondReject.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task RejectActionShowsErrorWhenStatusUpdateFails()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var inner = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            inner,
            canonicalUrl: "https://www.queenonline.com/news/reject-candidate",
            title: "Reject me",
            excerpt: "Excerpt",
            sourceFeedOrSiteUrl: null,
            relevanceScore: null,
            confidenceScore: null);
        var discoveryRepository = new ConfigurableNewsDiscoveryRepository(inner)
        {
            TryUpdateCandidateStatusHandler = (id, update, ct) =>
                update.Status == NewsCandidateStatus.Rejected
                    ? Task.FromResult(false)
                    : inner.TryUpdateCandidateStatusAsync(id, update, ct)
        };
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore, discoveryRepository: discoveryRepository);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/reject", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("could not be marked as rejected", reviewBody);
    }

    [Fact]
    public async Task IgnoreDuplicateActionShowsErrorWhenAlreadyIgnored()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var (_, secondId) = await NewsDiscoveryTestSeeder.SeedDuplicatePairAsync(discoveryRepository);
        await discoveryRepository.TryUpdateCandidateStatusAsync(
            secondId,
            new NewsCandidateStatusUpdate(
                NewsCandidateStatus.IgnoredDuplicate,
                DuplicateOfCandidateId: 1));
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore, discoveryRepository: discoveryRepository);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{secondId}/ignoreduplicate", secondId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{secondId}");
        Assert.Contains("already been ignored as a duplicate", reviewBody);
    }

    [Fact]
    public async Task IgnoreDuplicateActionShowsErrorWhenStatusUpdateFails()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var inner = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            inner,
            canonicalUrl: "https://www.queenonline.com/news/reject-candidate",
            title: "Reject me",
            excerpt: "Excerpt",
            sourceFeedOrSiteUrl: null,
            relevanceScore: null,
            confidenceScore: null);
        var discoveryRepository = new ConfigurableNewsDiscoveryRepository(inner)
        {
            TryUpdateCandidateStatusHandler = (id, update, ct) =>
                update.Status == NewsCandidateStatus.IgnoredDuplicate
                    ? Task.FromResult(false)
                    : inner.TryUpdateCandidateStatusAsync(id, update, ct)
        };
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore, discoveryRepository: discoveryRepository);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/ignoreduplicate", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("could not be marked as a duplicate", reviewBody);
    }

    [Fact]
    public async Task PromoteActionShowsErrorWhenDraftIsMissing()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var inner = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(inner);
        var discoveryRepository = new ConfigurableNewsDiscoveryRepository(inner)
        {
            GetDraftByCandidateIdHandler = (_, _) => Task.FromResult<NewsAgentDraft?>(null)
        };
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore, discoveryRepository: discoveryRepository);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Generate or save a draft before promoting", reviewBody);
    }

    [Fact]
    public async Task PromoteActionShowsConflictMessage_WhenConcurrencyExceptionIsThrown()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var inner = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(inner);
        var discoveryRepository = new ConfigurableNewsDiscoveryRepository(inner)
        {
            TryUpdateCandidateStatusHandler = (_, _, _) => throw new OptimisticConcurrencyException(),
        };
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore, discoveryRepository: discoveryRepository);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains(OptimisticConcurrencyException.UserMessage, reviewBody);
    }

    [Fact]
    public async Task PromoteActionShowsErrorWhenCandidateIsNotDrafted()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDiscoveredCandidateWithDraftAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore, discoveryRepository: discoveryRepository);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Only drafted candidates can be promoted", reviewBody);
    }

    [Fact]
    public async Task PromoteActionShowsErrorWhenDraftAcknowledgementFails()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var inner = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateWithDraftAsync(inner);
        var discoveryRepository = new ConfigurableNewsDiscoveryRepository(inner)
        {
            TryUpdateCandidateStatusHandler = (id, update, ct) =>
                update.Status == NewsCandidateStatus.Drafted
                    ? Task.FromResult(false)
                    : inner.TryUpdateCandidateStatusAsync(id, update, ct)
        };
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore, discoveryRepository: discoveryRepository);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("could not be marked as drafted before promotion", reviewBody);
    }

    [Fact]
    public async Task PromoteActionShowsErrorWhenCandidateStatusUpdateFails()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var inner = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(inner);
        var discoveryRepository = new ConfigurableNewsDiscoveryRepository(inner)
        {
            TryUpdateCandidateStatusHandler = (id, update, ct) =>
                update.Status == NewsCandidateStatus.PromotedToArticle
                    ? Task.FromResult(false)
                    : inner.TryUpdateCandidateStatusAsync(id, update, ct)
        };
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore, discoveryRepository: discoveryRepository);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Promotion failed while updating the discovery candidate", reviewBody);
    }

    [Fact]
    public async Task PromoteActionShowsErrorWhenCreateDraftFails()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        var newsStore = new SharedNewsStore();
        var adminRepository = new FailingCreateAdminNewsRepository(
            new InMemoryAdminNewsRepository(newsStore),
            new InvalidOperationException("Simulated create failure."));
        var client = CreateClient(
            AdminEmail,
            newsStore,
            discoveryStore,
            discoveryRepository: discoveryRepository,
            adminNewsRepository: adminRepository);

        var response = await PostActionAsync(client, $"/admin/news-discovery/{candidateId}/promote", candidateId);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Promotion failed while creating the admin draft", reviewBody);
    }

    [Fact]
    public async Task ReviewPageShowsDuplicateLinkWhenConfigured()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var (firstId, secondId) = await NewsDiscoveryTestSeeder.SeedDuplicatePairAsync(discoveryRepository);
        await discoveryRepository.TryUpdateCandidateStatusAsync(
            secondId,
            new NewsCandidateStatusUpdate(
                NewsCandidateStatus.IgnoredDuplicate,
                DuplicateOfCandidateId: firstId));
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var body = await client.GetStringAsync($"/admin/news-discovery/{secondId}");
        Assert.Contains($"Candidate #{firstId}", body);
        Assert.Contains("Original Queen story", body);
    }

    [Fact]
    public async Task IndexFiltersByTrustTierAndHasDraft()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var filteredBody = await client.GetStringAsync("/admin/news-discovery?trustTier=Primary&hasDraft=true");
        Assert.Contains("Discovery review candidate", filteredBody);
        Assert.Contains($"/admin/news-discovery/{candidateId}", filteredBody);

        var emptyBody = await client.GetStringAsync("/admin/news-discovery?trustTier=Secondary&hasDraft=true");
        Assert.Contains("No candidates match the current filters.", emptyBody);
    }

    [Fact]
    public async Task IndexIgnoreActionRejectsCandidateAndHidesItFromDefaultQueue()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://www.queenonline.com/news/list-ignore",
            title: "List ignore candidate",
            excerpt: "Excerpt",
            sourceFeedOrSiteUrl: null);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var indexBody = await client.GetStringAsync("/admin/news-discovery");
        Assert.Contains("List ignore candidate", indexBody);
        Assert.Contains("Ignore / remove from queue", indexBody);

        var response = await PostIndexActionAsync(
            client,
            "/admin/news-discovery?handler=ignore",
            new Dictionary<string, string> { ["id"] = candidateId.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/news-discovery", response.Headers.Location!.OriginalString);

        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(candidate);
        Assert.Equal(NewsCandidateStatus.Rejected, candidate.Status);

        var defaultBody = await client.GetStringAsync("/admin/news-discovery");
        Assert.DoesNotContain("List ignore candidate", defaultBody);
        Assert.Contains("Ignored candidate", defaultBody);

        var rejectedBody = await client.GetStringAsync("/admin/news-discovery?status=Rejected");
        Assert.Contains("List ignore candidate", rejectedBody);
    }

    [Fact]
    public async Task AuthorizedAdminCanQueueOneTriageRunAndSeeItsStatus()
    {
        var appFactory = CreateFactory(new SharedNewsStore(), new SharedNewsDiscoveryStore());
        var client = CreateClientFromFactory(appFactory, AdminEmail);
        var repository = appFactory.Services.GetRequiredService<INewsAgentRunRequestRepository>();

        var indexBody = await client.GetStringAsync("/admin/news-discovery");
        Assert.Contains("Queue news gathering", indexBody);
        Assert.Contains("Submit article URL", indexBody);
        Assert.Contains("Force triage", indexBody);
        Assert.Contains("Drafts are generated only when you select an individual candidate", indexBody);

        var firstResponse = await PostIndexActionAsync(
            client,
            "/admin/news-discovery?handler=queuerun",
            []);
        var secondResponse = await PostIndexActionAsync(
            client,
            "/admin/news-discovery?handler=queuerun",
            []);
        var recent = await repository.ListRecentAsync();

        Assert.Equal(HttpStatusCode.Redirect, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, secondResponse.StatusCode);
        var request = Assert.Single(recent);
        Assert.Equal(AdminEmail, request.RequestedBy);
        Assert.Equal(NewsAgentRunRequestStatus.Pending, request.Status);

        var statusBody = await client.GetStringAsync("/admin/news-discovery");
        Assert.Contains("already pending", statusBody);
        Assert.Contains(AdminEmail, statusBody);
        Assert.Contains("Recent runner requests (1)", statusBody);
        Assert.Contains("processing-history records, not news articles", statusBody);
    }

    [Fact]
    public async Task AuthorizedAdminCanQueueUrlIngestionDefaultingToTriageOnly()
    {
        var appFactory = CreateFactory(new SharedNewsStore(), new SharedNewsDiscoveryStore());
        var client = CreateClientFromFactory(appFactory, AdminEmail);
        var repository = appFactory.Services.GetRequiredService<INewsAgentRunRequestRepository>();

        var response = await PostIndexActionAsync(
            client,
            "/admin/news-discovery?handler=queueurlingestion",
            new Dictionary<string, string>
            {
                ["ArticleUrl"] = "https://www.queenonline.com/news/manual-url",
                ["UrlIngestionAction"] = "triage"
            });
        var recent = await repository.ListRecentAsync();

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var request = Assert.Single(recent);
        Assert.Equal(NewsAgentRunRequestKind.UrlIngestion, request.Kind);
        Assert.Equal("https://www.queenonline.com/news/manual-url", request.ArticleUrl);
        Assert.False(request.GenerateDraft);

        var statusBody = await client.GetStringAsync("/admin/news-discovery");
        Assert.Contains("no draft generation", statusBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthorizedAdminCanQueueUrlIngestionWithExplicitDraftOverride()
    {
        var appFactory = CreateFactory(new SharedNewsStore(), new SharedNewsDiscoveryStore());
        var client = CreateClientFromFactory(appFactory, AdminEmail);
        var repository = appFactory.Services.GetRequiredService<INewsAgentRunRequestRepository>();

        var response = await PostIndexActionAsync(
            client,
            "/admin/news-discovery?handler=queueurlingestion",
            new Dictionary<string, string>
            {
                ["ArticleUrl"] = "https://www.queenonline.com/news/manual-url-draft",
                ["UrlIngestionAction"] = "triage-and-draft"
            });
        var request = Assert.Single(await repository.ListRecentAsync());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(request.GenerateDraft);
        Assert.Equal(NewsAgentRunRequestKind.UrlIngestion, request.Kind);
    }

    [Fact]
    public async Task UrlIngestion_rejects_private_urls_without_queueing()
    {
        var appFactory = CreateFactory(new SharedNewsStore(), new SharedNewsDiscoveryStore());
        var client = CreateClientFromFactory(appFactory, AdminEmail);
        var repository = appFactory.Services.GetRequiredService<INewsAgentRunRequestRepository>();

        var response = await PostIndexActionAsync(
            client,
            "/admin/news-discovery?handler=queueurlingestion",
            new Dictionary<string, string>
            {
                ["ArticleUrl"] = "http://127.0.0.1/secret",
                ["UrlIngestionAction"] = "triage"
            });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(await repository.ListRecentAsync());
        var body = await client.GetStringAsync("/admin/news-discovery");
        Assert.Contains("blocked", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IndexBulkIgnoreRejectsAllCurrentlyListedCandidates()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var primaryId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://www.queenonline.com/news/bulk-ignore-primary",
            title: "Bulk ignore primary",
            excerpt: "Excerpt",
            sourceKey: "bulk-primary",
            sourceName: "Bulk Primary",
            sourceFeedOrSiteUrl: null,
            trustTier: NewsDiscoveryTrustTier.Primary);
        var secondaryId = await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://www.queenonline.com/news/bulk-ignore-secondary",
            title: "Bulk ignore secondary",
            excerpt: "Excerpt",
            sourceKey: "bulk-secondary",
            sourceName: "Bulk Secondary",
            sourceFeedOrSiteUrl: null,
            trustTier: NewsDiscoveryTrustTier.Secondary);
        var client = CreateClient(AdminEmail, new SharedNewsStore(), discoveryStore);

        var filteredBody = await client.GetStringAsync("/admin/news-discovery?trustTier=Primary");
        Assert.Contains("Bulk ignore primary", filteredBody);
        Assert.DoesNotContain("Bulk ignore secondary", filteredBody);
        Assert.Contains("Ignore this page / remove from queue", filteredBody);

        var response = await PostIndexActionAsync(
            client,
            "/admin/news-discovery?handler=ignorelisted",
            new Dictionary<string, string> { ["trustTier"] = "Primary" },
            "/admin/news-discovery?trustTier=Primary");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/news-discovery?trustTier=Primary", response.Headers.Location!.OriginalString);

        var primary = await discoveryRepository.GetCandidateByIdAsync(primaryId);
        var secondary = await discoveryRepository.GetCandidateByIdAsync(secondaryId);
        Assert.NotNull(primary);
        Assert.NotNull(secondary);
        Assert.Equal(NewsCandidateStatus.Rejected, primary.Status);
        Assert.Equal(NewsCandidateStatus.NeedsReview, secondary.Status);

        var defaultBody = await client.GetStringAsync("/admin/news-discovery");
        Assert.DoesNotContain("Bulk ignore primary", defaultBody);
        Assert.Contains("Bulk ignore secondary", defaultBody);
        Assert.Contains("Ignored 1 listed candidate", defaultBody);
    }

    [Fact]
    public async Task DiscoveryIndex_pages_candidates_and_preserves_filters()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var baseTime = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

        await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://example.com/filter-one",
            title: "Filter page one",
            discoveredAt: baseTime.AddMinutes(3));
        await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://example.com/filter-two",
            title: "Filter page two",
            discoveredAt: baseTime.AddMinutes(2));
        await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            canonicalUrl: "https://example.com/filter-three",
            title: "Filter page three",
            discoveredAt: baseTime.AddMinutes(1));

        var client = CreateClient(AdminEmail, discoveryStore: discoveryStore);

        var firstPage = await client.GetStringAsync("/admin/news-discovery?status=NeedsReview&pageSize=2");
        Assert.Contains("Filter page one", firstPage);
        Assert.Contains("Filter page two", firstPage);
        Assert.DoesNotContain("Filter page three", firstPage);
        Assert.Contains("Showing 1–2 of 3 candidates", firstPage);
        Assert.Contains("page=2", firstPage);
        Assert.Contains("status=NeedsReview", firstPage);
        Assert.Contains("pageSize=2", firstPage);

        var secondPage = await client.GetStringAsync("/admin/news-discovery?status=NeedsReview&pageSize=2&page=2");
        Assert.Contains("Filter page three", secondPage);
        Assert.DoesNotContain("Filter page one", secondPage);
        Assert.Contains("Showing 3–3 of 3 candidates", secondPage);
    }

    [Fact]
    public async Task DiscoveryIndex_clamps_page_size_and_redirects_out_of_range_page()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        await NewsDiscoveryTestSeeder.SeedNeedsReviewCandidateAsync(
            discoveryRepository,
            title: "Clamped page candidate");
        var client = CreateClient(AdminEmail, discoveryStore: discoveryStore);

        var clampedPage = await client.GetStringAsync("/admin/news-discovery?status=NeedsReview&pageSize=500");
        Assert.Contains("Clamped page candidate", clampedPage);

        var outOfRangeResponse = await client.GetAsync("/admin/news-discovery?status=NeedsReview&page=99");
        Assert.Equal(HttpStatusCode.Redirect, outOfRangeResponse.StatusCode);
        Assert.Equal("/admin/news-discovery?status=NeedsReview", outOfRangeResponse.Headers.Location!.OriginalString);
    }

    private WebApplicationFactory<Program> CreateFactory(
        SharedNewsStore newsStore,
        SharedNewsDiscoveryStore discoveryStore,
        string? openRouterApiKey = "test-key",
        string? draftResponseJson = null,
        bool aiClientEnabled = true,
        INewsDiscoveryRepository? discoveryRepository = null,
        IAdminNewsRepository? adminNewsRepository = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            if (openRouterApiKey is not null)
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["OpenRouter:ApiKey"] = openRouterApiKey
                    });
                });
            }

            var draftJson = draftResponseJson ?? SampleDraftJson;
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SharedNewsStore>();
                services.RemoveAll<SharedNewsDiscoveryStore>();
                services.RemoveAll<INewsRepository>();
                services.RemoveAll<IAdminNewsRepository>();
                services.RemoveAll<INewsAuditRepository>();
                services.RemoveAll<INewsDiscoveryRepository>();
                services.RemoveAll<INewsAiClient>();
                services.AddSingleton(newsStore);
                services.AddSingleton(discoveryStore);
                services.AddSingleton<INewsRepository>(_ => new QueenZone.Data.InMemoryNewsRepository(newsStore));
                services.AddSingleton<INewsAuditRepository>(_ => new InMemoryNewsAuditRepository(newsStore));
                if (adminNewsRepository is not null)
                {
                    services.AddSingleton(adminNewsRepository);
                }
                else
                {
                    services.AddSingleton<IAdminNewsRepository>(_ => new InMemoryAdminNewsRepository(newsStore));
                }

                if (discoveryRepository is not null)
                {
                    services.AddSingleton(discoveryRepository);
                }
                else
                {
                    services.AddSingleton<INewsDiscoveryRepository>(_ => new InMemoryNewsDiscoveryRepository(discoveryStore));
                }
                services.AddSingleton<INewsAiClient>(_ => new RegenerateDraftFakeAiClient(draftJson, aiClientEnabled));
            });
        });

    private HttpClient CreateClient(
        string? email = null,
        SharedNewsStore? newsStore = null,
        SharedNewsDiscoveryStore? discoveryStore = null,
        string? openRouterApiKey = "test-key",
        string? draftResponseJson = null,
        bool aiClientEnabled = true,
        INewsDiscoveryRepository? discoveryRepository = null,
        IAdminNewsRepository? adminNewsRepository = null)
    {
        newsStore ??= new SharedNewsStore();
        discoveryStore ??= new SharedNewsDiscoveryStore();
        var appFactory = CreateFactory(
            newsStore,
            discoveryStore,
            openRouterApiKey,
            draftResponseJson,
            aiClientEnabled,
            discoveryRepository,
            adminNewsRepository);
        return CreateClientFromFactory(appFactory, email);
    }

    private static HttpClient CreateClientFromFactory(WebApplicationFactory<Program> appFactory, string? email)
    {
        var client = appFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false
        });

        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, email);
        }

        return client;
    }

    private static async Task<HttpResponseMessage> PostActionAsync(HttpClient client, string actionPath, int candidateId)
    {
        var reviewPage = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        var token = ExtractAntiforgeryToken(reviewPage);
        return await client.PostAsync(actionPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [AdminNewsDiscoveryPageModel.AntiforgeryTokenFieldName] = token
        }));
    }

    private static async Task<HttpResponseMessage> PostAdminNewsActionAsync(HttpClient client, string actionPath)
    {
        var listPage = await client.GetStringAsync("/admin/news");
        var token = ExtractAntiforgeryToken(listPage);
        return await client.PostAsync(actionPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [QueenZone.Web.Pages.Admin.News.AdminNewsPageModel.AntiforgeryTokenFieldName] = token
        }));
    }

    private static async Task<HttpResponseMessage> PostIndexActionAsync(
        HttpClient client,
        string actionPath,
        Dictionary<string, string> fields,
        string indexPath = "/admin/news-discovery")
    {
        var indexPage = await client.GetStringAsync(indexPath);
        fields[AdminNewsDiscoveryPageModel.AntiforgeryTokenFieldName] = ExtractAntiforgeryToken(indexPage);
        return await client.PostAsync(actionPath, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> PostDraftEditAsync(
        HttpClient client,
        int candidateId,
        Dictionary<string, string> fields)
    {
        var formPage = await client.GetStringAsync($"/admin/news-discovery/{candidateId}/edit-draft");
        fields[AdminNewsDiscoveryPageModel.AntiforgeryTokenFieldName] = ExtractAntiforgeryToken(formPage);
        return await client.PostAsync($"/admin/news-discovery/{candidateId}/edit-draft", new FormUrlEncodedContent(fields));
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken" value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();

    private sealed class RegenerateDraftFakeAiClient(string content, bool enabled = true) : INewsAiClient
    {
        public bool IsEnabled { get; } = enabled;

        public Task<NewsAiChatCompletion> CompleteChatAsync(
            NewsAiChatRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new NewsAiChatCompletion(
                content,
                "openai/gpt-4.1-mini",
                1,
                1,
                0.0001m,
                false));
    }
}
