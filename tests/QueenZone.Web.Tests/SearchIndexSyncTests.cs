using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Verifies publish/create write paths immediately keep the unified search index in
/// sync, rather than relying solely on the next scheduled batch reindex.
/// </summary>
[Collection(AdminNewsDeleteErrorCollection.Name)]
public sealed class SearchIndexSyncTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public SearchIndexSyncTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task PublishingNewsArticle_MakesItImmediatelySearchable()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var createResponse = await AdminHttpTestHelpers.PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Search sync publish title",
                ["excerpt"] = "Created to exercise search index sync.",
                ["body"] = "Plain text body for search index sync.",
                ["publishedAt"] = "2026-06-14",
            });
        var articleId = AdminHttpTestHelpers.ParseNewsIdFromEditRedirect(createResponse);

        var beforePublish = await client.GetStringAsync("/search?q=search+sync+publish");
        Assert.DoesNotContain("Search sync publish title", beforePublish);

        var publishResponse = await AdminHttpTestHelpers.PostNewsActionAsync(client, $"/admin/news/{articleId}/publish");
        Assert.Equal(HttpStatusCode.Redirect, publishResponse.StatusCode);

        var afterPublish = await client.GetStringAsync("/search?q=search+sync+publish");
        Assert.Contains("Search sync publish title", afterPublish);
    }

    [Fact]
    public async Task UnpublishingNewsArticle_RemovesItFromSearchImmediately()
    {
        var client = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);

        var createResponse = await AdminHttpTestHelpers.PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Search sync unpublish title",
                ["excerpt"] = "Created to exercise search index sync.",
                ["body"] = "Plain text body for search index sync.",
                ["publishedAt"] = "2026-06-14",
            });
        var articleId = AdminHttpTestHelpers.ParseNewsIdFromEditRedirect(createResponse);

        await AdminHttpTestHelpers.PostNewsActionAsync(client, $"/admin/news/{articleId}/publish");
        var afterPublish = await client.GetStringAsync("/search?q=search+sync+unpublish");
        Assert.Contains("Search sync unpublish title", afterPublish);

        var unpublishResponse = await AdminHttpTestHelpers.PostNewsActionAsync(client, $"/admin/news/{articleId}/unpublish");
        Assert.Equal(HttpStatusCode.Redirect, unpublishResponse.StatusCode);

        var afterUnpublish = await client.GetStringAsync("/search?q=search+sync+unpublish&type=news");
        Assert.DoesNotContain("Search sync unpublish title", afterUnpublish);
    }

    [Fact]
    public async Task CreatingForumThread_MakesItImmediatelySearchable()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());
        const string title = "Search sync forum create title";
        var topicPath = await PostNewThreadAsync(client, title);

        Assert.StartsWith("/forum/topic/", topicPath, StringComparison.Ordinal);

        var afterCreate = await client.GetStringAsync("/search?q=search+sync+forum+create");
        Assert.Contains(title, afterCreate);
        Assert.Contains(topicPath, afterCreate);
    }

    [Fact]
    public async Task ReplyingToForumThread_UpdatesSearchLastActivity()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());
        const string title = "Search sync forum reply activity title";
        var topicPath = await PostNewThreadAsync(client, title);

        var store = factory.Services.GetRequiredService<SharedSearchIndexStore>();
        var document = Assert.Single(store.GetAll(), item => item.Title == title);
        document.PublishedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var stalePublishedAt = document.PublishedAt;

        var page = await client.GetStringAsync(topicPath);
        var token = ExtractAntiforgeryToken(page);
        var replyResponse = await client.PostAsync(topicPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "<p>Reply should bump search last activity.</p>",
        }));
        Assert.Equal(HttpStatusCode.Redirect, replyResponse.StatusCode);

        var updated = Assert.Single(store.GetAll(), item => item.Title == title);
        Assert.True(updated.PublishedAt > stalePublishedAt);
    }

    [Fact]
    public async Task CreatingForumThread_StillSucceeds_WhenSearchIndexFails()
    {
        var failingFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISearchIndexService>();
                services.AddSingleton<ISearchIndexService>(new ThrowingSearchIndexService());
            });
        });
        var client = CreateMemberClient(failingFactory, Guid.NewGuid());

        var topicPath = await PostNewThreadAsync(client, "Search sync forum failure title");

        Assert.StartsWith("/forum/topic/", topicPath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishingNewsArticle_StillSucceeds_WhenSearchIndexFails()
    {
        var failingFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISearchIndexService>();
                services.AddSingleton<ISearchIndexService>(new ThrowingSearchIndexService());
            });
        });
        var client = AdminHttpTestHelpers.CreateClient(failingFactory, AdminHttpTestHelpers.AdminEmail);

        var createResponse = await AdminHttpTestHelpers.PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = "Search sync publish failure title",
                ["excerpt"] = "Created to exercise search index sync failure.",
                ["body"] = "Plain text body for search index sync failure.",
                ["publishedAt"] = "2026-06-14",
            });
        var articleId = AdminHttpTestHelpers.ParseNewsIdFromEditRedirect(createResponse);

        var publishResponse = await AdminHttpTestHelpers.PostNewsActionAsync(client, $"/admin/news/{articleId}/publish");
        Assert.Equal(HttpStatusCode.Redirect, publishResponse.StatusCode);

        var unpublishResponse = await AdminHttpTestHelpers.PostNewsActionAsync(client, $"/admin/news/{articleId}/unpublish");
        Assert.Equal(HttpStatusCode.Redirect, unpublishResponse.StatusCode);
    }

    private static async Task<string> PostNewThreadAsync(HttpClient client, string title)
    {
        var form = await client.GetStringAsync("/forum/c/the-music/new-thread");
        var token = ExtractAntiforgeryToken(form);
        var response = await client.PostAsync("/forum/c/the-music/new-thread", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Subject"] = title,
            ["Body"] = "<p>Created to exercise forum search index sync.</p>",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }

    private static HttpClient CreateMemberClient(WebApplicationFactory<Program> sourceFactory, Guid memberId)
    {
        var client = sourceFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, memberId.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Forum Fan");
        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var input = Regex.Match(
            html,
            """<input[^>]*name="__RequestVerificationToken"[^>]*>""",
            RegexOptions.IgnoreCase);
        Assert.True(input.Success, "Antiforgery token input was not found in the form.");

        var value = Regex.Match(input.Value, "value=\"(?<token>[^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(value.Success, "Antiforgery token value was not found in the form.");
        return value.Groups["token"].Value;
    }

    private sealed class ThrowingSearchIndexService : ISearchIndexService
    {
        public Task UpsertAsync(SearchDocumentEntity document, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");

        public Task RemoveAsync(string sourceKey, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");

        public Task ReplaceContentTypeAsync(
            string contentType,
            IReadOnlyList<SearchDocumentEntity> documents,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");

        public Task<IReadOnlyDictionary<string, int>> GetContentTypeCountsAsync(
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated index failure.");
    }
}
