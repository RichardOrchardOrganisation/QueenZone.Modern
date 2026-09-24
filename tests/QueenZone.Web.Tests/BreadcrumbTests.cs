using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class BreadcrumbTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public BreadcrumbTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ForumCategoryPageRendersBreadcrumbsAndStructuredData()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum/1/the-music");

        Assert.Contains("qz-breadcrumbs", body);
        Assert.Contains("href=\"/forum\"", body);
        Assert.Contains(">Forum<", body);
        Assert.Contains("aria-current=\"page\">The Music</span>", body);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", body);
        Assert.Contains("\"name\":\"The Music\"", body);
    }

    [Fact]
    public async Task ForumTopicPageRendersBreadcrumbsAndStructuredData()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");

        Assert.Contains("qz-breadcrumbs", body);
        Assert.Contains("href=\"/forum\"", body);
        Assert.Contains("href=\"/forum/1/the-music\"", body);
        Assert.Contains(">Forum<", body);
        Assert.Contains(">The Music<", body);
        Assert.Contains("aria-current=\"page\">Ranking every studio album</span>", body);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", body);
    }

    [Fact]
    public async Task BiographyDetailRendersBreadcrumbsAndStructuredData()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/biography/1/1946-1969");

        Assert.Contains("qz-breadcrumbs", body);
        Assert.Contains("href=\"/biography\"", body);
        Assert.Contains(">Biography<", body);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", body);
    }

    [Fact]
    public async Task PhotographyCategoryPageRendersBreadcrumbsAndStructuredData()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/photography/brian-may");

        Assert.Contains("qz-breadcrumbs", body);
        Assert.Contains("href=\"/photography\"", body);
        Assert.Contains(">Photography<", body);
        Assert.Contains("aria-current=\"page\">Brian May</span>", body);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/photography/brian-may"), body);
    }

    [Fact]
    public async Task PhotographyDetailPageRendersBreadcrumbsAndStructuredData()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/photography/brian-may/101");

        Assert.Contains("qz-breadcrumbs", body);
        Assert.Contains("href=\"/photography\"", body);
        Assert.Contains("href=\"/photography/brian-may\"", body);
        Assert.Contains(">Photography<", body);
        Assert.Contains(">Brian May<", body);
        Assert.Contains("aria-current=\"page\">Brian in action with his guitar</span>", body);
        Assert.Contains("\"@type\":\"BreadcrumbList\"", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/photography/brian-may/101"), body);
    }

    [Fact]
    public async Task BreadcrumbJsonLdItemUrlsAreAbsolute()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/news/1003/queenzone-modernisation-begins");

        Assert.Contains($"\"item\":\"{TestSiteConfiguration.PublicBaseUrl}/news\"", body);
    }
}
