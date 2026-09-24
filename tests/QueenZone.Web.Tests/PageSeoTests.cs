using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace QueenZone.Web.Tests;

public sealed class PageSeoTests : IClassFixture<PreviewPublicBaseUrlWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public PageSeoTests(PreviewPublicBaseUrlWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("/", "<title>QueenZone</title>", "The complete fan resource for Queen")]
    [InlineData("/news", "<title>QueenZone news</title>", "The latest Queen news")]
    [InlineData("/articles", "<title>QueenZone articles</title>", "In-depth Queen articles")]
    [InlineData("/photography", "<title>Photography | QueenZone</title>", "Browse Queen photograph collections")]
    [InlineData("/fan-performances", "<title>Fan Performances | QueenZone</title>", "Fan recordings of Queen songs")]
    [InlineData("/discography", "<title>Discography | QueenZone</title>", "Every Queen studio album")]
    [InlineData("/forum", "<title>Forum | QueenZone</title>", "Read-only Queenzone forum archive")]
    [InlineData("/biography", "<title>QueenZone biography</title>", "The story of Queen")]
    [InlineData("/timeline", "<title>Queen History Timeline", "Five decades of Queen history")]
    [InlineData("/trivia", "<title>Queen Trivia | QueenZone</title>", "A random Queen trivia fact")]
    public async Task PublicPage_HasExpectedTitleAndDescription(string path, string expectedTitleTag, string expectedDescriptionFragment)
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(path);

        Assert.Contains(expectedTitleTag, body);
        Assert.Contains($"<meta name=\"description\" content=\"", body);
        Assert.Contains(expectedDescriptionFragment, body);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/news")]
    [InlineData("/articles")]
    [InlineData("/photography")]
    [InlineData("/biography")]
    [InlineData("/photography/brian-may")]
    [InlineData("/photography/brian-may/101")]
    public async Task PublicPage_EmitsOpenGraphTags(string path)
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(path);

        Assert.Contains("<meta property=\"og:site_name\" content=\"QueenZone\">", body);
        Assert.Contains("<meta property=\"og:type\"", body);
        Assert.Contains("<meta property=\"og:url\" content=\"https://preview.queenzone.test/", body);
        Assert.Contains("<meta property=\"og:title\"", body);
        Assert.Contains("<meta property=\"og:description\"", body);
        Assert.Contains("<meta name=\"twitter:card\"", body);
    }

    [Fact]
    public async Task PhotographyDetailPage_EmitsOgImage()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/photography/brian-may/101");

        Assert.Contains("<meta property=\"og:image\"", body);
        Assert.Contains("summary_large_image", body);
    }

    [Fact]
    public async Task PhotographyCategoryPage_EmitsOgImageFromCover()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/photography/brian-may");

        Assert.Contains("<meta property=\"og:image\"", body);
    }

    [Fact]
    public async Task PublicPage_OgUrlUsesConfiguredPublicBaseUrl()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("<meta property=\"og:url\" content=\"https://preview.queenzone.test/\">", body);
    }
}
