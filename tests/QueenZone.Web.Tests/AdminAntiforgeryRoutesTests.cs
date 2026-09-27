using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminAntiforgeryRoutesTests : IClassFixture<WebHostVariantCache>, IAsyncLifetime
{
    private readonly VariantWebApplicationFactory factory;

    public AdminAntiforgeryRoutesTests(WebHostVariantCache variants)
    {
        factory = variants.Get(WebHostVariants.IsolatedAdminNews);
    }

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task News_create_without_antiforgery_token_returns_bad_request()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var response = await AdminHttpTestHelpers.PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Missing token article",
                ["excerpt"] = "Excerpt",
                ["body"] = "Body",
                ["publishedAt"] = "2026-06-14"
            },
            includeAntiforgeryToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task News_publish_without_antiforgery_token_returns_bad_request()
    {
        factory.AdminNews!.Seed(
        [
            new AdminNewsArticle(
                1,
                "Draft",
                "draft",
                "Excerpt",
                "Body",
                new DateTime(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc),
                null,
                false,
                null,
                null,
                null)
        ]);

        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var response = await AdminHttpTestHelpers.PostNewsActionAsync(
            client,
            "/admin/news/1/publish",
            includeAntiforgeryToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Discovery_promote_without_antiforgery_token_returns_bad_request()
    {
        var discoveryRepository = factory.Services.GetRequiredService<INewsDiscoveryRepository>();
        var candidateId = await SeedDraftedCandidateAsync(discoveryRepository);

        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var response = await AdminHttpTestHelpers.PostDiscoveryActionAsync(
            client,
            $"/admin/news-discovery/{candidateId}/promote",
            candidateId,
            includeAntiforgeryToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Discovery_queue_run_without_antiforgery_token_returns_bad_request()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var response = await client.PostAsync(
            "/admin/news-discovery?handler=queuerun",
            new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Discovery_queue_url_ingestion_without_antiforgery_token_returns_bad_request()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var response = await client.PostAsync(
            "/admin/news-discovery?handler=queueurlingestion",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["ArticleUrl"] = "https://www.queenonline.com/news/example",
                ["UrlIngestionAction"] = "triage"
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<int> SeedDraftedCandidateAsync(INewsDiscoveryRepository discoveryRepository)
    {
        var sourceId = await discoveryRepository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "antiforgery-source",
            "Antiforgery Source",
            "https://example.com/",
            null,
            NewsDiscoverySourceType.AllowlistedPage,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));

        var discoveredAt = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var candidateId = await discoveryRepository.CreateCandidateAsync(new NewsCandidateCreateRequest(
            sourceId,
            "https://example.com/antiforgery",
            "Antiforgery candidate",
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
        await discoveryRepository.UpsertDraftAsync(
            candidateId,
            new NewsAgentDraftUpsert(
                "Draft title",
                "draft-title",
                "Excerpt",
                "Body",
                null,
                null,
                null,
                discoveredAt.Date,
                null));

        return candidateId;
    }
}
