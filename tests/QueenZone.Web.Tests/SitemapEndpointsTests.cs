using System.Xml.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class SitemapEndpointsTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string BaseUrl = "https://www.queenzone.org";
    private readonly WebApplicationFactory<Program> factory;

    public SitemapEndpointsTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task RobotsTxt_ReferencesProductionSitemapAndBlocksAdmin()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/robots.txt");

        Assert.Contains($"Sitemap: {BaseUrl}/sitemap.xml", body);
        Assert.Contains("Disallow: /admin/", body);
        Assert.Contains("Disallow: /health", body);
    }

    [Fact]
    public async Task SitemapIndex_ReferencesSectionAndForumChildSitemaps()
    {
        var client = factory.CreateClient();

        var xml = await client.GetStringAsync("/sitemap.xml");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        var locations = document
            .Descendants(ns + "loc")
            .Select(element => element.Value)
            .ToList();

        Assert.Equal(
            [
                $"{BaseUrl}/sitemap-core.xml",
                $"{BaseUrl}/sitemap-news.xml",
                $"{BaseUrl}/sitemap-articles.xml",
                $"{BaseUrl}/sitemap-biography.xml",
                $"{BaseUrl}/sitemap-forum-categories.xml",
                $"{BaseUrl}/sitemap-photography.xml",
                $"{BaseUrl}/sitemap-fan-performances.xml",
                $"{BaseUrl}/sitemap-discography.xml",
                $"{BaseUrl}/sitemap-forum-1.xml"
            ],
            locations);
    }

    [Fact]
    public async Task ForumSitemap_IncludesStarterThreadCanonicalUrlsOnly()
    {
        var client = factory.CreateClient();

        var xml = await client.GetStringAsync("/sitemap-forum-1.xml");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        var locations = document
            .Descendants(ns + "loc")
            .Select(element => element.Value)
            .ToList();

        Assert.Contains($"{BaseUrl}/forum/topic/1002/ranking-every-studio-album", locations);
        Assert.DoesNotContain(locations, location => location.Contains("/page/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ForumSitemap_ReturnsNotFoundForOutOfRangeFileNumber()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/sitemap-forum-2.xml");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CoreSitemap_IncludesEvergreenCanonicalUrlsOnly()
    {
        var client = factory.CreateClient();

        var xml = await client.GetStringAsync("/sitemap-core.xml");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        var locations = document
            .Descendants(ns + "loc")
            .Select(element => element.Value)
            .ToList();

        Assert.Contains($"{BaseUrl}/", locations);
        Assert.Contains($"{BaseUrl}/about", locations);
        Assert.Contains($"{BaseUrl}/contact", locations);
        Assert.Contains($"{BaseUrl}/links", locations);
        Assert.Contains($"{BaseUrl}/mobile-apps", locations);
        Assert.DoesNotContain($"{BaseUrl}/news", locations);
        Assert.DoesNotContain($"{BaseUrl}/articles", locations);
        Assert.DoesNotContain($"{BaseUrl}/photography", locations);
    }

    [Fact]
    public async Task NewsSitemap_IncludesPublishedNewsUrlsOnly()
    {
        var client = factory.CreateClient();

        var xml = await client.GetStringAsync("/sitemap-news.xml");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        var locations = document
            .Descendants(ns + "loc")
            .Select(element => element.Value)
            .ToList();

        Assert.Contains($"{BaseUrl}/news", locations);
        Assert.Contains($"{BaseUrl}/news/page/2", locations);
        Assert.Contains($"{BaseUrl}/news/1003/queenzone-modernisation-begins", locations);
        Assert.DoesNotContain($"{BaseUrl}/news/9001/hidden-moderation-draft", locations);
    }

    [Fact]
    public async Task ArticlesSitemap_IncludesPublishedArticleUrlsOnly()
    {
        var client = factory.CreateClient();

        var xml = await client.GetStringAsync("/sitemap-articles.xml");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        var locations = document
            .Descendants(ns + "loc")
            .Select(element => element.Value)
            .ToList();

        Assert.Contains($"{BaseUrl}/articles", locations);
        Assert.Contains($"{BaseUrl}/articles/101/inside-the-making-of-bohemian-rhapsody", locations);
        Assert.DoesNotContain($"{BaseUrl}/articles/9001/hidden-moderation-draft", locations);
    }

    [Fact]
    public async Task BiographySitemap_IncludesBiographyUrls()
    {
        var client = factory.CreateClient();

        var xml = await client.GetStringAsync("/sitemap-biography.xml");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        var locations = document
            .Descendants(ns + "loc")
            .Select(element => element.Value)
            .ToList();

        Assert.Contains($"{BaseUrl}/biography", locations);
        Assert.Contains($"{BaseUrl}/biography/1/1946-1969", locations);
    }

    [Fact]
    public async Task ForumCategoriesSitemap_IncludesForumCategoryUrls()
    {
        var client = factory.CreateClient();

        var xml = await client.GetStringAsync("/sitemap-forum-categories.xml");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        var locations = document
            .Descendants(ns + "loc")
            .Select(element => element.Value)
            .ToList();

        Assert.Contains($"{BaseUrl}/forum", locations);
        Assert.Contains($"{BaseUrl}/forum/1/the-music", locations);
    }

    [Fact]
    public async Task PhotographySitemap_IncludesPhotographyUrlsOnly()
    {
        var client = factory.CreateClient();

        var xml = await client.GetStringAsync("/sitemap-photography.xml");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        var locations = document
            .Descendants(ns + "loc")
            .Select(element => element.Value)
            .ToList();

        Assert.Contains($"{BaseUrl}/photography", locations);
        Assert.Contains($"{BaseUrl}/photography/brian-may", locations);
        Assert.Contains($"{BaseUrl}/photography/queen", locations);
        Assert.Contains($"{BaseUrl}/photography/queen/201", locations);
        Assert.DoesNotContain($"{BaseUrl}/photography/empty-category", locations);
    }

    [Fact]
    public async Task NewsSitemap_UsesPublishedDateForNewsDetailLastMod()
    {
        var client = factory.CreateClient();

        var xml = await client.GetStringAsync("/sitemap-news.xml");
        var document = XDocument.Parse(xml);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

        var newsDetail = document
            .Descendants(ns + "url")
            .Single(url => url.Element(ns + "loc")?.Value == $"{BaseUrl}/news/1003/queenzone-modernisation-begins");

        Assert.Equal("2026-06-11", newsDetail.Element(ns + "lastmod")?.Value);
    }

    [Fact]
    public async Task CoreSitemap_SetsPublicCacheControlHeader()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/sitemap-core.xml");

        response.EnsureSuccessStatusCode();
        Assert.Equal("public, max-age=86400", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task SectionSitemap_SetsPublicCacheControlHeader()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/sitemap-photography.xml");

        response.EnsureSuccessStatusCode();
        Assert.Equal("public, max-age=86400", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task PhotographySitemap_IncludesPhotographyDetailUrlsFromBulkProjection()
    {
        var client = factory.CreateClient();

        var xml = await client.GetStringAsync("/sitemap-photography.xml");

        Assert.Contains($"{BaseUrl}/photography/brian-may", xml);
        Assert.Contains($"{BaseUrl}/photography/brian-may/101", xml);
        Assert.Contains($"{BaseUrl}/photography/queen/201", xml);
    }
}
