using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class PublicRssFeedTests : IClassFixture<PreviewPublicBaseUrlWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public PublicRssFeedTests(PreviewPublicBaseUrlWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task NewsFeed_ReturnsRssWithPublishedItemsOnly()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(NewsRoutes.FeedPath);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/rss+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<rss version=\"2.0\"", body);
        Assert.Contains("<title>QueenZone News</title>", body);
        Assert.Contains("QueenZone modernisation begins", body);
        Assert.Contains("/news/1003/queenzone-modernisation-begins", body);
        Assert.Contains("The first local vertical slice", body);
        Assert.DoesNotContain("Hidden moderation draft", body);
        Assert.Contains("https://preview.queenzone.test/news/feed.rss", body);
    }

    [Fact]
    public async Task NewsFeed_ItemLinksMatchCanonicalDetailRoutes()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(NewsRoutes.FeedPath);

        Assert.Contains(
            "<link>https://preview.queenzone.test/news/1003/queenzone-modernisation-begins</link>",
            body);
        Assert.Contains(
            "<guid isPermaLink=\"true\">https://preview.queenzone.test/news/1003/queenzone-modernisation-begins</guid>",
            body);
    }

    [Fact]
    public async Task ArticlesFeed_IncludesArchiveArticlesWithCanonicalUrls()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(ArticlesRoutes.FeedPath);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/rss+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<title>QueenZone Articles</title>", body);
        Assert.Contains("Inside the Making of Bohemian Rhapsody", body);
        Assert.Contains("/articles/101/inside-the-making-of-bohemian-rhapsody", body);
        Assert.DoesNotContain("Hidden moderation draft", body);
    }

    [Fact]
    public async Task ArticlesFeed_IncludesCommunityArticles()
    {
        var community = new PublishedArticleSubmission(
            Guid.NewGuid(),
            "Community RSS Feature",
            "community-rss-feature",
            "Community excerpt for feed.",
            "<p>Body</p>",
            null,
            null,
            DateTimeOffset.UtcNow.AddHours(-1),
            "Author",
            40);

        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Site:PublicBaseUrl"] = "https://preview.queenzone.test",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IArticleRepository>(new FixedArticleRepository([community]));
            });
        }).CreateClient();

        var body = await client.GetStringAsync(ArticlesRoutes.FeedPath);

        Assert.Contains("Community RSS Feature", body);
        Assert.Contains("/articles/community-rss-feature", body);
        Assert.Contains("Community excerpt for feed.", body);
        // Archive seed items remain available alongside community.
        Assert.Contains("Inside the Making of Bohemian Rhapsody", body);
    }

    private sealed class FixedArticleRepository(IEnumerable<PublishedArticleSubmission> seed) : IArticleRepository
    {
        private readonly IReadOnlyList<PublishedArticleSubmission> items = [.. seed];

        public Task<int> GetCountAsync(string? tag = null, CancellationToken ct = default) =>
            Task.FromResult(items.Count);

        public Task<IReadOnlyList<PublishedArticleSubmission>> GetPageAsync(
            int page, int pageSize, string? tag = null, CancellationToken ct = default) =>
            Task.FromResult(items);

        public Task<PublishedArticleSubmission?> GetBySlugAsync(string slug, CancellationToken ct = default) =>
            Task.FromResult(items.FirstOrDefault(a => a.Slug == slug));

        public Task<(PublishedArticleSubmission? Previous, PublishedArticleSubmission? Next)> GetAdjacentAsync(
            DateTimeOffset publishedAt, CancellationToken ct = default) =>
            Task.FromResult<(PublishedArticleSubmission?, PublishedArticleSubmission?)>((null, null));

        public Task<IReadOnlyList<PublishedArticleSubmission>> GetSitemapEntriesAsync(CancellationToken ct = default) =>
            Task.FromResult(items);
    }

    [Fact]
    public async Task NewsIndex_ExposesAlternateRssLink()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/news");

        Assert.Contains(
            "rel=\"alternate\" type=\"application/rss+xml\" title=\"QueenZone News\" href=\"https://preview.queenzone.test/news/feed.rss\"",
            body);
    }

    [Fact]
    public async Task ArticlesIndex_ExposesAlternateRssLink()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/articles");

        Assert.Contains(
            "rel=\"alternate\" type=\"application/rss+xml\" title=\"QueenZone Articles\" href=\"https://preview.queenzone.test/articles/feed.rss\"",
            body);
    }
}
