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
    [InlineData("/", "QueenZone", "The complete fan resource for Queen")]
    [InlineData("/news", "QueenZone news", "The latest Queen news")]
    [InlineData("/articles", "QueenZone articles", "In-depth Queen articles")]
    [InlineData("/photography", "Photography | QueenZone", "Browse Queen photograph collections")]
    [InlineData("/fan-performances", "Fan Performances | QueenZone", "Fan recordings of Queen songs")]
    [InlineData("/discography", "Discography | QueenZone", "Every Queen studio album")]
    [InlineData("/forum", "Forum | QueenZone", "QueenZone community discussions and archive")]
    [InlineData("/biography", "QueenZone biography", "The story of Queen")]
    [InlineData("/timeline", "Queen History Timeline · Queenzone", "Five decades of Queen history")]
    [InlineData("/trivia", "Queen Trivia | QueenZone", "A random Queen trivia fact")]
    public async Task PublicPage_HasExpectedTitleAndDescription(string path, string expectedTitle, string expectedDescriptionFragment)
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(path);

        TestHtmlAssertions.AssertPageTitle(body, expectedTitle);
        Assert.Contains(expectedDescriptionFragment, TestHtmlAssertions.MetaName(body, "description"));
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

        TestHtmlAssertions.AssertMetaProperty(body, "og:site_name", "QueenZone");
        Assert.NotEmpty(TestHtmlAssertions.MetaProperty(body, "og:type"));
        Assert.StartsWith("https://preview.queenzone.test/", TestHtmlAssertions.MetaProperty(body, "og:url"), StringComparison.Ordinal);
        Assert.NotEmpty(TestHtmlAssertions.MetaProperty(body, "og:title"));
        Assert.NotEmpty(TestHtmlAssertions.MetaProperty(body, "og:description"));
        Assert.NotEmpty(TestHtmlAssertions.MetaName(body, "twitter:card"));
    }

    [Fact]
    public async Task PhotographyDetailPage_EmitsOgImage()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/photography/brian-may/101");

        Assert.NotEmpty(TestHtmlAssertions.MetaProperty(body, "og:image"));
        TestHtmlAssertions.AssertMetaName(body, "twitter:card", "summary_large_image");
    }

    [Fact]
    public async Task PhotographyCategoryPage_EmitsOgImageFromCover()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/photography/brian-may");

        Assert.NotEmpty(TestHtmlAssertions.MetaProperty(body, "og:image"));
    }

    [Fact]
    public async Task PublicPage_OgUrlUsesConfiguredPublicBaseUrl()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/");

        TestHtmlAssertions.AssertMetaProperty(body, "og:url", "https://preview.queenzone.test/");
    }
}
