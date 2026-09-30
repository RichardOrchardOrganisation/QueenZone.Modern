using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace QueenZone.Web.Tests;

public sealed class AbsoluteCanonicalUrlTests : IClassFixture<PreviewPublicBaseUrlWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public AbsoluteCanonicalUrlTests(PreviewPublicBaseUrlWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task HomePageUsesConfiguredPublicBaseUrlForCanonical()
    {
        var client = factory.CreateClient();

        var home = await client.GetStringAsync("/");

        Assert.Contains(
            """<link rel="canonical" href="https://preview.queenzone.test/">""",
            home);
    }

    [Fact]
    public async Task NewsArchiveUsesConfiguredPublicBaseUrlForCanonicalAndPaginationLinks()
    {
        var client = factory.CreateClient();

        var pageOne = await client.GetStringAsync("/news");
        var pageTwo = await client.GetStringAsync("/news/page/2");

        Assert.Contains(
            """<link rel="canonical" href="https://preview.queenzone.test/news">""",
            pageOne);
        Assert.Contains(
            """<link rel="canonical" href="https://preview.queenzone.test/news/page/2">""",
            pageTwo);
        Assert.Contains(
            """<link rel="next" href="https://preview.queenzone.test/news/page/2">""",
            pageOne);
        Assert.Contains(
            """<link rel="prev" href="https://preview.queenzone.test/news">""",
            pageTwo);
    }

    [Theory]
    [InlineData("/", "https://preview.queenzone.test/")]
    [InlineData("/about", "https://preview.queenzone.test/about")]
    [InlineData("/contact", "https://preview.queenzone.test/contact")]
    [InlineData("/forum", "https://preview.queenzone.test/forum")]
    [InlineData("/biography", "https://preview.queenzone.test/biography")]
    [InlineData("/photography", "https://preview.queenzone.test/photography")]
    [InlineData("/discography", "https://preview.queenzone.test/discography")]
    [InlineData("/fan-performances", "https://preview.queenzone.test/fan-performances")]
    [InlineData("/timeline", "https://preview.queenzone.test/timeline")]
    [InlineData("/trivia", "https://preview.queenzone.test/trivia")]
    [InlineData("/articles", "https://preview.queenzone.test/articles")]
    [InlineData("/privacy", "https://preview.queenzone.test/privacy")]
    public async Task PublicSectionIndexesEmitSelfReferentialCanonical(string path, string expectedCanonical)
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(path);

        Assert.Contains($"""<link rel="canonical" href="{expectedCanonical}">""", body);
    }

    [Theory]
    [InlineData("/forum/1/the-music", "https://preview.queenzone.test/forum/1/the-music")]
    [InlineData("/forum/topic/1002/ranking-every-studio-album", "https://preview.queenzone.test/forum/topic/1002/ranking-every-studio-album")]
    [InlineData("/photography/brian-may", "https://preview.queenzone.test/photography/brian-may")]
    [InlineData("/photography/brian-may/101", "https://preview.queenzone.test/photography/brian-may/101")]
    [InlineData("/discography/albums/4/a-night-at-the-opera", "https://preview.queenzone.test/discography/albums/4/a-night-at-the-opera")]
    [InlineData("/biography/2/1970", "https://preview.queenzone.test/biography/2/1970")]
    public async Task PublicDetailPagesEmitSelfReferentialCanonical(string path, string expectedCanonical)
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync(path);

        Assert.Contains($"""<link rel="canonical" href="{expectedCanonical}">""", body);
    }

    [Fact]
    public async Task ArticlesArchivePageTwo_CanonicalizesToOwnPage()
    {
        var client = factory.CreateClient();

        var pageTwo = await client.GetStringAsync("/articles/page/2");

        Assert.Contains(
            """<link rel="canonical" href="https://preview.queenzone.test/articles/page/2">""",
            pageTwo);
    }
}
