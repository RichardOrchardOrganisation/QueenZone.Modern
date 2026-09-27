using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminNewsDiscoveryEfRoutesTests : IClassFixture<AdminEfWebApplicationFactory>, IAsyncLifetime
{
    private readonly AdminEfWebApplicationFactory factory;

    public AdminNewsDiscoveryEfRoutesTests(AdminEfWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Ef_backed_promote_creates_admin_draft_and_updates_candidate()
    {
        var candidateId = await SeedDraftedCandidateAsync();

        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var promoteResponse = await AdminHttpTestHelpers.PostDiscoveryActionAsync(
            client,
            $"/admin/news-discovery/{candidateId}/promote",
            candidateId);

        var articleId = AdminHttpTestHelpers.ParseNewsIdFromEditRedirect(promoteResponse);

        await using var scope = factory.Services.CreateAsyncScope();
        var adminRepository = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
        var discoveryRepository = scope.ServiceProvider.GetRequiredService<INewsDiscoveryRepository>();

        var article = await adminRepository.GetByIdAsync(articleId);
        Assert.NotNull(article);
        Assert.Equal("Discovery draft title", article.Title);
        Assert.False(article.IsPublished);

        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(candidate);
        Assert.Equal(NewsCandidateStatus.PromotedToArticle, candidate.Status);
        Assert.Equal(articleId, candidate.PromotedNewsId);
    }

    [Fact]
    public async Task Ef_backed_reject_from_needs_review_shows_no_silent_redirect()
    {
        var candidateId = await SeedNeedsReviewCandidateAsync();

        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var rejectResponse = await AdminHttpTestHelpers.PostDiscoveryActionAsync(
            client,
            $"/admin/news-discovery/{candidateId}/reject",
            candidateId);
        Assert.Equal(HttpStatusCode.Redirect, rejectResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var discoveryRepository = scope.ServiceProvider.GetRequiredService<INewsDiscoveryRepository>();
        var candidate = await discoveryRepository.GetCandidateByIdAsync(candidateId);
        Assert.NotNull(candidate);
        Assert.Equal(NewsCandidateStatus.Rejected, candidate.Status);
    }

    [Fact]
    public async Task Ef_backed_invalid_transition_shows_error_message()
    {
        var candidateId = await SeedPromotedCandidateAsync();

        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var rejectResponse = await AdminHttpTestHelpers.PostDiscoveryActionAsync(
            client,
            $"/admin/news-discovery/{candidateId}/reject",
            candidateId);
        Assert.Equal(HttpStatusCode.Redirect, rejectResponse.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Cannot transition candidate status", reviewBody);
    }

    [Fact]
    public async Task Ef_backed_promote_rolls_back_when_candidate_update_fails()
    {
        var candidateId = await SeedDraftedCandidateAsync();
        factory.PromoteGate.FailPromoteStatusUpdate = true;

        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var promoteResponse = await AdminHttpTestHelpers.PostDiscoveryActionAsync(
            client,
            $"/admin/news-discovery/{candidateId}/promote",
            candidateId);
        Assert.Equal(HttpStatusCode.Redirect, promoteResponse.StatusCode);

        var reviewBody = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");
        Assert.Contains("Promotion failed while updating the discovery candidate", reviewBody);

        await using var scope = factory.Services.CreateAsyncScope();
        var adminRepository = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
        var articles = await adminRepository.GetAllAsync();
        Assert.DoesNotContain(articles, article => article.Title == "Discovery draft title");
    }

    private async Task<int> SeedDraftedCandidateAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var discoveryRepository = scope.ServiceProvider.GetRequiredService<INewsDiscoveryRepository>();

        var sourceId = await discoveryRepository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "ef-test-source",
            "EF Test Source",
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
            "https://example.com/ef-candidate",
            "EF discovery candidate",
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
                "Discovery draft title",
                "discovery-draft-title",
                "Discovery excerpt",
                "Discovery body",
                null,
                null,
                null,
                discoveredAt.Date,
                null));

        return candidateId;
    }

    private async Task<int> SeedNeedsReviewCandidateAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var discoveryRepository = scope.ServiceProvider.GetRequiredService<INewsDiscoveryRepository>();

        var sourceId = await discoveryRepository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
            "ef-reject-source",
            "EF Reject Source",
            "https://example.com/",
            null,
            NewsDiscoverySourceType.AllowlistedPage,
            NewsDiscoveryTrustTier.Primary,
            60,
            true,
            null));

        var discoveredAt = new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc);
        return await discoveryRepository.CreateCandidateAsync(new NewsCandidateCreateRequest(
            sourceId,
            "https://example.com/ef-reject",
            "Reject candidate",
            discoveredAt,
            "Excerpt",
            discoveredAt));
    }

    private async Task<int> SeedPromotedCandidateAsync()
    {
        var candidateId = await SeedDraftedCandidateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var adminRepository = scope.ServiceProvider.GetRequiredService<IAdminNewsRepository>();
        var discoveryRepository = scope.ServiceProvider.GetRequiredService<INewsDiscoveryRepository>();

        var newsId = await adminRepository.CreateDraftAsync(
            new AdminNewsDraft(
                "Promoted placeholder",
                "promoted-placeholder",
                "Excerpt",
                "Body",
                new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                null),
            AdminHttpTestHelpers.AdminEmail);

        await discoveryRepository.TryUpdateCandidateStatusAsync(
            candidateId,
            new NewsCandidateStatusUpdate(
                NewsCandidateStatus.PromotedToArticle,
                PromotedNewsId: newsId));

        return candidateId;
    }
}
