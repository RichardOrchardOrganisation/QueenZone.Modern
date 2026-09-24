using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin.NewsDiscovery;

namespace QueenZone.Web.Tests;

public sealed partial class AdminNewsAgentGuidanceRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> factory;

    public AdminNewsAgentGuidanceRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AnonymousUserCannotAccessPromptSettings()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/admin/news-discovery/prompt-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedNonAdminCannotAccessPromptSettings()
    {
        var client = CreateClient("stranger@example.com");

        var response = await client.GetAsync("/admin/news-discovery/prompt-settings");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MemberCookieCannotAccessPromptSettings()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Forum Fan");

        var response = await client.GetAsync("/admin/news-discovery/prompt-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminCanSavePublishRollbackAndRestoreDefault()
    {
        var store = new SharedNewsAgentGuidanceStore();
        var repository = new InMemoryNewsAgentGuidanceRepository(store);
        var client = CreateClient(AdminEmail, store);

        var indexBody = await client.GetStringAsync("/admin/news-discovery");
        Assert.Contains("/admin/news-discovery/prompt-settings", indexBody, StringComparison.Ordinal);

        var page = await client.GetStringAsync("/admin/news-discovery/prompt-settings");
        Assert.Contains("future NewsAgent runs only", page, StringComparison.Ordinal);
        Assert.Contains("Using compiled default", page, StringComparison.Ordinal);
        Assert.Contains("--- BEGIN ADMIN EDITORIAL GUIDANCE (untrusted) ---", page, StringComparison.Ordinal);
        Assert.Contains("You triage discovered news items", page, StringComparison.Ordinal);
        Assert.Contains("You draft QueenZone news articles", page, StringComparison.Ordinal);
        Assert.Contains("action=\"/admin/news-discovery/prompt-settings?handler=SaveDraft\"", page, StringComparison.Ordinal);
        Assert.Contains("action=\"/admin/news-discovery/prompt-settings?handler=Publish\"", page, StringComparison.Ordinal);
        Assert.Contains("action=\"/admin/news-discovery/prompt-settings?handler=RestoreDefault\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", page, StringComparison.Ordinal);
        // With no draft or published overlay yet, each textarea is pre-filled with that
        // type's own default editorial guidance so there is real text to tune - not blank.
        var overlayTextareas = OverlayTextareaRegex().Matches(page);
        Assert.Equal(2, overlayTextareas.Count);
        var triageTextarea = overlayTextareas[0].Groups["content"].Value;
        var draftTextarea = overlayTextareas[1].Groups["content"].Value;
        Assert.Contains("You triage discovered news items", triageTextarea, StringComparison.Ordinal);
        Assert.DoesNotContain("You draft QueenZone news articles", triageTextarea, StringComparison.Ordinal);
        Assert.Contains("You draft QueenZone news articles", draftTextarea, StringComparison.Ordinal);
        Assert.DoesNotContain("You triage discovered news items", draftTextarea, StringComparison.Ordinal);

        // The fixed JSON/safety contract is always added in code and must never be
        // shown as editable overlay text.
        Assert.DoesNotContain("Respond with JSON only", triageTextarea, StringComparison.Ordinal);
        Assert.DoesNotContain("Respond with JSON only", draftTextarea, StringComparison.Ordinal);

        var save = await PostAsync(client, "/admin/news-discovery/prompt-settings?handler=SaveDraft", new Dictionary<string, string>
        {
            ["type"] = "Triage",
            ["content"] = "prefer member-news <script>alert(1)</script>",
            ["rowVersion"] = string.Empty
        });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        var afterSave = await client.GetStringAsync("/admin/news-discovery/prompt-settings");
        Assert.Contains("prefer member-news", afterSave, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", afterSave, StringComparison.Ordinal);
        Assert.Contains("Respond with JSON only", afterSave, StringComparison.Ordinal);
        Assert.Contains("--- BEGIN ADMIN EDITORIAL GUIDANCE (untrusted) ---", afterSave, StringComparison.Ordinal);

        var draft = await repository.GetDraftAsync(NewsAgentGuidanceType.Triage);
        Assert.NotNull(draft);
        var publishWithoutConfirm = await PostAsync(client, "/admin/news-discovery/prompt-settings?handler=Publish", new Dictionary<string, string>
        {
            ["type"] = "Triage",
            ["rowVersion"] = Convert.ToBase64String(draft.RowVersion)
        });
        Assert.Equal(HttpStatusCode.OK, publishWithoutConfirm.StatusCode);
        Assert.Contains("Confirm this action", await publishWithoutConfirm.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var publish = await PostAsync(client, "/admin/news-discovery/prompt-settings?handler=Publish", new Dictionary<string, string>
        {
            ["type"] = "Triage",
            ["rowVersion"] = Convert.ToBase64String(draft.RowVersion),
            ["confirm"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);

        var published = await repository.GetPublishedAsync(NewsAgentGuidanceType.Triage);
        Assert.NotNull(published);
        Assert.Equal("prefer member-news <script>alert(1)</script>", published.Content);

        var replacement = await repository.SaveDraftAsync(NewsAgentGuidanceType.Triage, "replacement overlay", AdminEmail, null);
        await repository.PublishDraftAsync(NewsAgentGuidanceType.Triage, AdminEmail, replacement.RowVersion);

        var rollback = await PostAsync(client, "/admin/news-discovery/prompt-settings?handler=Rollback", new Dictionary<string, string>
        {
            ["type"] = "Triage",
            ["revisionId"] = published.Id.ToString(),
            ["confirm"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, rollback.StatusCode);
        var rolledBack = await repository.GetPublishedAsync(NewsAgentGuidanceType.Triage);
        Assert.Equal(published.Content, rolledBack!.Content);
        Assert.NotEqual(published.Id, rolledBack.Id);

        var restore = await PostAsync(client, "/admin/news-discovery/prompt-settings?handler=RestoreDefault", new Dictionary<string, string>
        {
            ["type"] = "Triage",
            ["confirm"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, restore.StatusCode);
        Assert.Equal(string.Empty, (await repository.GetPublishedAsync(NewsAgentGuidanceType.Triage))!.Content);
    }

    [Fact]
    public async Task AuthorizedAdminCanEditAndPublishBothOverlays()
    {
        var store = new SharedNewsAgentGuidanceStore();
        var repository = new InMemoryNewsAgentGuidanceRepository(store);
        var client = CreateClient(AdminEmail, store);

        foreach (var (type, content) in new[]
        {
            (NewsAgentGuidanceType.Triage, "Edited triage overlay"),
            (NewsAgentGuidanceType.Draft, "Edited draft overlay")
        })
        {
            var save = await PostAsync(client, "/admin/news-discovery/prompt-settings?handler=SaveDraft", new Dictionary<string, string>
            {
                ["type"] = type.ToString(),
                ["content"] = content,
                ["rowVersion"] = string.Empty
            });
            Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

            var draft = await repository.GetDraftAsync(type);
            Assert.NotNull(draft);
            Assert.Equal(content, draft.Content);

            var publish = await PostAsync(client, "/admin/news-discovery/prompt-settings?handler=Publish", new Dictionary<string, string>
            {
                ["type"] = type.ToString(),
                ["rowVersion"] = Convert.ToBase64String(draft.RowVersion),
                ["confirm"] = "true"
            });
            Assert.Equal(HttpStatusCode.Redirect, publish.StatusCode);
            Assert.Equal(content, (await repository.GetPublishedAsync(type))!.Content);
        }
    }

    [Fact]
    public async Task SaveDraft_rejects_oversized_content()
    {
        var client = CreateClient(AdminEmail, new SharedNewsAgentGuidanceStore());

        var response = await PostAsync(client, "/admin/news-discovery/prompt-settings?handler=SaveDraft", new Dictionary<string, string>
        {
            ["type"] = "Draft",
            ["content"] = new string('a', 4001),
            ["rowVersion"] = string.Empty
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("4000", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_without_antiforgery_is_rejected()
    {
        var client = CreateClient(AdminEmail, new SharedNewsAgentGuidanceStore());

        var response = await client.PostAsync(
            "/admin/news-discovery/prompt-settings?handler=Publish",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["type"] = "Triage",
                ["confirm"] = "true"
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReviewPage_shows_guidance_revision_and_hash()
    {
        var discoveryStore = new SharedNewsDiscoveryStore();
        var discoveryRepository = new InMemoryNewsDiscoveryRepository(discoveryStore);
        var candidateId = await NewsDiscoveryTestSeeder.SeedDraftedCandidateAsync(discoveryRepository);
        await discoveryRepository.CreateAiRunAsync(new NewsAiRunCreateRequest(
            candidateId,
            NewsAiRunKind.Triage,
            "openrouter",
            "openai/gpt-4.1-nano",
            "triage-v2",
            DateTime.UtcNow,
            44,
            2,
            "abc123hash"));
        var client = CreateClient(AdminEmail, new SharedNewsAgentGuidanceStore(), discoveryStore);

        var body = await client.GetStringAsync($"/admin/news-discovery/{candidateId}");

        Assert.Contains("AI provenance", body, StringComparison.Ordinal);
        Assert.Contains("triage-v2", body, StringComparison.Ordinal);
        Assert.Contains("2", body, StringComparison.Ordinal);
        Assert.Contains("abc123hash", body, StringComparison.Ordinal);
    }

    private HttpClient CreateClient(
        string? email = null,
        SharedNewsAgentGuidanceStore? guidanceStore = null,
        SharedNewsDiscoveryStore? discoveryStore = null)
    {
        guidanceStore ??= new SharedNewsAgentGuidanceStore();
        discoveryStore ??= new SharedNewsDiscoveryStore();
        var appFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SharedNewsAgentGuidanceStore>();
                services.RemoveAll<INewsAgentGuidanceRepository>();
                services.RemoveAll<SharedNewsDiscoveryStore>();
                services.RemoveAll<INewsDiscoveryRepository>();
                services.AddSingleton(guidanceStore);
                services.AddSingleton<INewsAgentGuidanceRepository>(_ =>
                    new InMemoryNewsAgentGuidanceRepository(guidanceStore));
                services.AddSingleton(discoveryStore);
                services.AddSingleton<INewsDiscoveryRepository>(_ =>
                    new InMemoryNewsDiscoveryRepository(discoveryStore));
            });
        });

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

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string actionPath,
        Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync("/admin/news-discovery/prompt-settings");
        fields[AdminNewsDiscoveryPageModel.AntiforgeryTokenFieldName] = ExtractAntiforgeryToken(page);
        return await client.PostAsync(actionPath, new FormUrlEncodedContent(fields));
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken" value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();

    [GeneratedRegex("""<textarea name="content"[^>]*>(?<content>[\s\S]*?)</textarea>""", RegexOptions.IgnoreCase)]
    private static partial Regex OverlayTextareaRegex();
}
