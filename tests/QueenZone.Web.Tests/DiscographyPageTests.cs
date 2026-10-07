using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class DiscographyPageTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public DiscographyPageTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
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
        Assert.Contains("/songs", body);
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
    public async Task DiscographyAlbumDetail_RendersListenOnLinksForAlbumAndTracks()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/discography/albums/4/a-night-at-the-opera");

        // Album-level buttons in the hero, Spotify first.
        Assert.Contains("aria-label=\"Listen to A Night at the Opera\"", body);
        var spotify = body.IndexOf("href=\"https://open.spotify.com/album/0SampleNightAtTheOpera\"", StringComparison.Ordinal);
        var apple = body.IndexOf("href=\"https://music.apple.com/gb/album/a-night-at-the-opera/1000000004\"", StringComparison.Ordinal);
        Assert.True(spotify > 0 && apple > spotify, "Album links should render Spotify then Apple Music.");
        Assert.Contains("Listen on Spotify<span class=\"visually-hidden\">: A Night at the Opera (opens in a new tab)</span>", body);
        Assert.Contains("rel=\"noopener external\"", body);
        Assert.Contains("data-streaming-link=\"apple-music\"", body);
        Assert.Contains("data-streaming-target=\"album\"", body);

        // Track-level compact links only on the track that has them.
        Assert.Contains("href=\"https://open.spotify.com/track/0SampleBohemianRhapsod\"", body);
        Assert.Contains(": listen to Bohemian Rhapsody (opens in a new tab)", body);
        Assert.Equal(1, CountOccurrences(body, "qz-listen--compact"));
    }

    [Fact]
    public async Task DiscographyAlbumDetail_WithoutLinks_RendersNoListenOnMarkup()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/discography/albums/3/sheer-heart-attack");

        Assert.DoesNotContain("qz-listen", body);
        Assert.DoesNotContain("data-streaming-link", body);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0; index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
