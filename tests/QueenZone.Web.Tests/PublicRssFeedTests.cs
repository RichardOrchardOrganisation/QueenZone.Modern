using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class PublicRssFeedTests :
    IClassFixture<PreviewPublicBaseUrlWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>
{
    private readonly WebApplicationFactory<Program> factory;
    private readonly WebHostVariantCache variants;

    public PublicRssFeedTests(PreviewPublicBaseUrlWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        this.variants = variants;
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

        var host = variants.Get(WebHostVariants.PreviewPublicBaseUrlMutableCommunityArticles);
        await host.ResetAsync();
        host.CommunityArticles!.Seed([community]);
        var client = host.CreateClient();

        var body = await client.GetStringAsync(ArticlesRoutes.FeedPath);

        Assert.Contains("Community RSS Feature", body);
        Assert.Contains("/articles/community-rss-feature", body);
        Assert.Contains("Community excerpt for feed.", body);
        // Archive seed items remain available alongside community.
        Assert.Contains("Inside the Making of Bohemian Rhapsody", body);
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
