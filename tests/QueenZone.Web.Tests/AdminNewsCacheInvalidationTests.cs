using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin.News;

namespace QueenZone.Web.Tests;

/// <summary>
/// Verifies admin publish / unpublish / delete write paths invalidate public news query cache
/// used by homepage latest news and archive published counts.
/// </summary>
[Collection(AdminNewsDeleteErrorCollection.Name)]
public sealed partial class AdminNewsCacheInvalidationTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> factory;

    public AdminNewsCacheInvalidationTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Publish_invalidates_homepage_latest_news_cache()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        // Warm the public latest-news cache (homepage uses count 5).
        var homeBefore = await client.GetStringAsync("/");
        Assert.DoesNotContain("Cache invalidate publish title", homeBefore);

        var articleId = await CreateDraftAsync(client, "Cache invalidate publish title");
        var publishResponse = await PostActionAsync(client, $"/admin/news/{articleId}/publish");
        Assert.Equal(HttpStatusCode.Redirect, publishResponse.StatusCode);

        var homeAfter = await client.GetStringAsync("/");
        Assert.Contains("Cache invalidate publish title", homeAfter);
        Assert.Contains($"/news/{articleId}/cache-invalidate-publish-title", homeAfter);
    }

    [Fact]
    public async Task Unpublish_invalidates_homepage_latest_news_cache()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var articleId = await CreateDraftAsync(client, "Cache invalidate unpublish title");
        Assert.Equal(HttpStatusCode.Redirect, (await PostActionAsync(client, $"/admin/news/{articleId}/publish")).StatusCode);

        var homePublished = await client.GetStringAsync("/");
        Assert.Contains("Cache invalidate unpublish title", homePublished);

        Assert.Equal(HttpStatusCode.Redirect, (await PostActionAsync(client, $"/admin/news/{articleId}/unpublish")).StatusCode);

        var homeUnpublished = await client.GetStringAsync("/");
        Assert.DoesNotContain("Cache invalidate unpublish title", homeUnpublished);
    }

    [Fact]
    public async Task Delete_published_article_invalidates_homepage_latest_news_cache()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var articleId = await CreateDraftAsync(client, "Cache invalidate delete title");
        Assert.Equal(HttpStatusCode.Redirect, (await PostActionAsync(client, $"/admin/news/{articleId}/publish")).StatusCode);

        var homePublished = await client.GetStringAsync("/");
        Assert.Contains("Cache invalidate delete title", homePublished);

        Assert.Equal(HttpStatusCode.Redirect, (await PostActionAsync(client, $"/admin/news/{articleId}/delete")).StatusCode);

        var homeDeleted = await client.GetStringAsync("/");
        Assert.DoesNotContain("Cache invalidate delete title", homeDeleted);
    }

    [Fact]
    public async Task Publish_invalidates_news_archive_published_count_cache()
    {
        // Seed enough published items that archive paging uses the published-count cache.
        var seed = Enumerable.Range(1, NewsRoutes.ArchivePageSize + 1).Select(i => new AdminNewsArticle(
            i,
            $"Seed archive {i}",
            $"seed-archive-{i}",
            "Excerpt",
            "Body",
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i),
            null,
            true,
            null,
            null,
            null));
        var store = new SharedNewsStore(seed);
        var client = CreateClient(AdminEmail, store);

        var archiveBefore = await client.GetStringAsync("/news");
        Assert.Contains("Page 1 of 2", archiveBefore);
        Assert.DoesNotContain("Archive count cache publish", archiveBefore);

        var articleId = await CreateDraftAsync(client, "Archive count cache publish");
        Assert.Equal(HttpStatusCode.Redirect, (await PostActionAsync(client, $"/admin/news/{articleId}/publish")).StatusCode);

        var archiveAfter = await client.GetStringAsync("/news");
        Assert.Contains("Archive count cache publish", archiveAfter);
        // Count should reflect the newly published article (still 2 pages with page size 20).
        Assert.Contains("Page 1 of 2", archiveAfter);
    }

    [Fact]
    public async Task Edit_published_article_invalidates_homepage_latest_news_cache()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var articleId = await CreateDraftAsync(client, "Original cache edit title");
        Assert.Equal(HttpStatusCode.Redirect, (await PostActionAsync(client, $"/admin/news/{articleId}/publish")).StatusCode);

        var homeOriginal = await client.GetStringAsync("/");
        Assert.Contains("Original cache edit title", homeOriginal);

        var editResponse = await PostArticleAsync(
            client,
            $"/admin/news/{articleId}/edit",
            $"/admin/news/{articleId}",
            new Dictionary<string, string>
            {
                ["title"] = "Updated cache edit title",
                ["excerpt"] = "Updated excerpt for cache invalidation.",
                ["body"] = "Updated plain text body.",
                ["publishedAt"] = "2026-06-14"
            });
        Assert.Equal(HttpStatusCode.Redirect, editResponse.StatusCode);

        var homeUpdated = await client.GetStringAsync("/");
        Assert.DoesNotContain("Original cache edit title", homeUpdated);
        Assert.Contains("Updated cache edit title", homeUpdated);
    }

    [Fact]
    public async Task Publish_invalidates_news_sitemap_cache()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        // Warm output cache for the news sitemap.
        var sitemapBefore = await client.GetStringAsync("/sitemap-news.xml");
        Assert.DoesNotContain("sitemap-cache-publish-title", sitemapBefore, StringComparison.OrdinalIgnoreCase);

        var articleId = await CreateDraftAsync(client, "Sitemap cache publish title");
        Assert.Equal(HttpStatusCode.Redirect, (await PostActionAsync(client, $"/admin/news/{articleId}/publish")).StatusCode);

        var sitemapAfter = await client.GetStringAsync("/sitemap-news.xml");
        Assert.Contains($"/news/{articleId}/sitemap-cache-publish-title", sitemapAfter);
    }

    [Fact]
    public async Task Unpublish_invalidates_news_sitemap_cache()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var articleId = await CreateDraftAsync(client, "Sitemap cache unpublish title");
        Assert.Equal(HttpStatusCode.Redirect, (await PostActionAsync(client, $"/admin/news/{articleId}/publish")).StatusCode);

        var sitemapPublished = await client.GetStringAsync("/sitemap-news.xml");
        Assert.Contains($"/news/{articleId}/sitemap-cache-unpublish-title", sitemapPublished);

        Assert.Equal(HttpStatusCode.Redirect, (await PostActionAsync(client, $"/admin/news/{articleId}/unpublish")).StatusCode);

        var sitemapUnpublished = await client.GetStringAsync("/sitemap-news.xml");
        Assert.DoesNotContain($"/news/{articleId}/sitemap-cache-unpublish-title", sitemapUnpublished);
    }

    [Fact]
    public async Task Edit_published_article_invalidates_news_sitemap_cache()
    {
        var store = new SharedNewsStore();
        var client = CreateClient(AdminEmail, store);

        var articleId = await CreateDraftAsync(client, "Sitemap original edit title");
        Assert.Equal(HttpStatusCode.Redirect, (await PostActionAsync(client, $"/admin/news/{articleId}/publish")).StatusCode);

        var sitemapOriginal = await client.GetStringAsync("/sitemap-news.xml");
        Assert.Contains($"/news/{articleId}/sitemap-original-edit-title", sitemapOriginal);

        var editResponse = await PostArticleAsync(
            client,
            $"/admin/news/{articleId}/edit",
            $"/admin/news/{articleId}",
            new Dictionary<string, string>
            {
                ["title"] = "Sitemap updated edit title",
                ["excerpt"] = "Updated excerpt for sitemap cache invalidation.",
                ["body"] = "Updated plain text body.",
                ["publishedAt"] = "2026-06-14"
            });
        Assert.Equal(HttpStatusCode.Redirect, editResponse.StatusCode);

        var sitemapUpdated = await client.GetStringAsync("/sitemap-news.xml");
        Assert.DoesNotContain($"/news/{articleId}/sitemap-original-edit-title", sitemapUpdated);
        Assert.Contains($"/news/{articleId}/sitemap-updated-edit-title", sitemapUpdated);
    }

    private async Task<int> CreateDraftAsync(HttpClient client, string title)
    {
        var createResponse = await PostArticleAsync(
            client,
            "/admin/news/new",
            "/admin/news",
            new Dictionary<string, string>
            {
                ["title"] = title,
                ["excerpt"] = "Created to exercise cache invalidation.",
                ["body"] = "Plain text body for cache invalidation.",
                ["publishedAt"] = "2026-06-14"
            });

        Assert.Equal(HttpStatusCode.Redirect, createResponse.StatusCode);
        var editPath = createResponse.Headers.Location!.OriginalString;
        Assert.Matches("/admin/news/\\d+/edit", editPath);
        return int.Parse(editPath.Split('/')[3], System.Globalization.CultureInfo.InvariantCulture);
    }

    private HttpClient CreateClient(string email, SharedNewsStore store)
    {
        var appFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<SharedNewsStore>();
                services.RemoveAll<INewsRepository>();
                services.RemoveAll<IAdminNewsRepository>();
                services.RemoveAll<INewsAuditRepository>();
                services.RemoveAll<INewsDiscoveryRepository>();
                services.RemoveAll<SharedNewsDiscoveryStore>();
                services.AddSingleton(store);
                services.AddSingleton<INewsRepository>(_ => new QueenZone.Data.InMemoryNewsRepository(store));
                services.AddSingleton<IAdminNewsRepository>(_ => new InMemoryAdminNewsRepository(store));
                services.AddSingleton<INewsAuditRepository>(_ => new InMemoryNewsAuditRepository(store));
                services.AddSingleton<SharedNewsDiscoveryStore>();
                services.AddSingleton<INewsDiscoveryRepository, InMemoryNewsDiscoveryRepository>();
            }));

        var client = appFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, email);
        return client;
    }

    private static async Task<HttpResponseMessage> PostArticleAsync(
        HttpClient client,
        string formPath,
        string postPath,
        Dictionary<string, string> fields)
    {
        var formPage = await client.GetStringAsync(formPath);
        fields[AdminNewsPageModel.AntiforgeryTokenFieldName] = ExtractAntiforgeryToken(formPage);
        return await client.PostAsync(postPath, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> PostActionAsync(HttpClient client, string actionPath)
    {
        var listPage = await client.GetStringAsync("/admin/news");
        var token = ExtractAntiforgeryToken(listPage);
        return await client.PostAsync(actionPath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            [AdminNewsPageModel.AntiforgeryTokenFieldName] = token
        }));
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken" value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
