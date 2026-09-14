using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class DiscographyPageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public DiscographyPageTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task DiscographyIndexRendersSeedAlbums()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/discography");

        Assert.Contains("Discography", body);
        Assert.Contains("The core catalogue.", body);
        Assert.DoesNotContain("The complete studio catalogue, restored sleeve by sleeve.", body);
        Assert.Contains("A Night at the Opera", body);
        Assert.Contains("https://cdn.queenzone.org/images/discography/", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/discography"), body);
    }

    [Fact]
    public async Task DiscographyIndexLinksToRareDiscography()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/discography");

        Assert.Contains("/discography/rare-discography", body);
        Assert.Contains("John S Stuart", body);
    }

    [Fact]
    public async Task RareDiscographyPage_RendersLegacyDiscographyThreads()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/discography/rare-discography");

        Assert.Contains("Rare Discography", body);
        Assert.Contains("John S Stuart", body);
        Assert.Contains("Complete guide to Queen promo-only pressings", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/discography/rare-discography"), body);
    }

    [Fact]
    public async Task DiscographyAlbumDetail_RendersTracklist()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/discography/albums/4/a-night-at-the-opera");

        Assert.Contains("Death on Two Legs", body);
        Assert.Contains("Seaside Rendezvous", body);
        Assert.Contains("Bohemian Rhapsody", body);
        // The closing track must still render even though the first track carries lyrics
        // text containing markup-like content - regression guard for the lyrics-breaking-
        // the-tracklist bug (raw lyrics HTML was previously closing the surrounding <ol>).
        Assert.Contains("God Save the Queen", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/discography/albums/4/a-night-at-the-opera"), body);
    }

    [Fact]
    public async Task DiscographyAlbumDetail_RendersGeneralNotesAsHtmlInsteadOfEscapingIt()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/discography/albums/4/a-night-at-the-opera");

        Assert.Contains("<strong>'Bohemian Rhapsody'</strong>", body);
        Assert.DoesNotContain("&lt;strong&gt;", body);

        var metaDescriptionStart = body.IndexOf("name=\"description\"", StringComparison.Ordinal);
        Assert.True(metaDescriptionStart >= 0);
        var metaDescriptionTag = body.Substring(metaDescriptionStart, 200);
        Assert.DoesNotContain("&lt;p&gt;", metaDescriptionTag);
        Assert.DoesNotContain("<p>", metaDescriptionTag);
    }

    [Fact]
    public async Task DiscographyAlbumDetail_EscapesLyricsInsteadOfRenderingMarkup()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/discography/albums/4/a-night-at-the-opera");

        Assert.Contains("View lyrics", body);
        Assert.Contains("&lt;/li&gt;&lt;/ol&gt;", body);
        Assert.DoesNotContain("</li></ol>", body);
    }

    [Fact]
    public async Task DiscographyAlbumDetail_RedirectsWhenSlugStale()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/discography/albums/4/wrong-slug");

        Assert.Equal(System.Net.HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/discography/albums/4/a-night-at-the-opera", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task DiscographyAlbumDetail_ReturnsNotFound_WhenAlbumMissing()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/discography/albums/999/missing");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
