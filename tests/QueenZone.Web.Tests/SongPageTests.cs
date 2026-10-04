using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class SongPageTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public SongPageTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task SongsIndex_RendersCanonicalTitlesAndDedupeCount()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/songs");

        Assert.Contains("Songs", body);
        Assert.Contains("Bohemian Rhapsody", body);
        Assert.Contains("/songs/bohemian-rhapsody", body);
        Assert.Contains("Seven Seas of Rhye", body);
        Assert.Contains("2 appearances", body);
        Assert.Contains("Death on Two Legs (Dedicated to...)", body);
        Assert.Contains("/songs/death-on-two-legs-dedicated-to", body);
        Assert.DoesNotContain("/songs/death-on-two-legs\"", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/songs"), body);
    }

    [Fact]
    public async Task SongDetail_RendersAppearancesAndEscapesLyrics()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/songs/keep-yourself-alive");

        Assert.Contains("Keep Yourself Alive", body);
        Assert.Contains("/discography/albums/1/queen", body);
        Assert.Contains("&lt;/li&gt;&lt;/ol&gt;", body);
        Assert.DoesNotContain("</li></ol>", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/songs/keep-yourself-alive"), body);
    }

    [Fact]
    public async Task SongDetail_RedirectsWhenSlugCaseDiffers()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/songs/Bohemian-Rhapsody");

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/songs/bohemian-rhapsody", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task SongDetail_ReturnsNotFound_WhenSlugUnknown()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/songs/not-a-queen-song");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SongDetail_ShowsRelatedContent_WhenSearchTitleMatches()
    {
        using var scope = factory.Services.CreateScope();
        var searchIndex = scope.ServiceProvider.GetRequiredService<ISearchIndexService>();
        await searchIndex.UpsertAsync(new SearchDocumentEntity
        {
            SourceKey = "news:song-related-test",
            ContentType = SiteSearchContentType.News,
            Title = "Bohemian Rhapsody",
            Body = "A news item about the song.",
            Summary = "A news item about the song.",
            Url = "/news/1003/queenzone-modernisation-begins",
        });

        try
        {
            var client = factory.CreateClient();
            var body = await client.GetStringAsync("/songs/bohemian-rhapsody");

            Assert.Contains("News", body);
            Assert.Contains("/news/1003/queenzone-modernisation-begins", body);
        }
        finally
        {
            await searchIndex.RemoveAsync("news:song-related-test");
        }
    }

    [Fact]
    public async Task AlbumTracklist_LinksEachTitleToItsSongPage()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/discography/albums/4/a-night-at-the-opera");

        Assert.Contains("href=\"/songs/bohemian-rhapsody\"", body);
        Assert.Contains("href=\"/songs/death-on-two-legs-dedicated-to\"", body);
    }
}
