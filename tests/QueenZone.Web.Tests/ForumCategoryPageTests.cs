using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class ForumCategoryPageTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ForumCategoryPageTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ForumCategoryPageRendersSeedTopics()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum/1/the-music");

        Assert.Contains("The Music", body);
        Assert.Contains("Ranking every studio album", body);
        Assert.Contains("/forum/topic/1002/ranking-every-studio-album", body);
        Assert.Contains("Forum Guidelines", body);
        Assert.Contains("<strong>30</strong> threads", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/forum/1/the-music"), body);
    }

    [Fact]
    public async Task ForumCategoryPageTwoIncludesPagination()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum/1/the-music/page/2");

        Assert.Contains("Page 2 of 2", body);
        Assert.Contains("Archive sample thread 1030", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/forum/1/the-music/page/2"), body);
    }

    [Fact]
    public async Task ForumCategoryPageRedirectsPageOneToCanonicalCategoryUrl()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/forum/1/the-music/page/1");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/forum/1/the-music", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ForumCategoryPageReturnsNotFoundForMissingCategory()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/forum/9999/missing-board");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
