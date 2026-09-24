using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class LinksRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public LinksRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task LinksPageRendersAvailableLinksByCategory()
    {
        var client = CreateClientWithLinks(
        [
            new QueenLinkCategory(
                1,
                "Official",
                [
                    new QueenLink(1, "Queen Online", "https://www.queenonline.com/", "Official Queen site.", 1, true),
                ]),
        ]);

        var body = await client.GetStringAsync("/links");

        Assert.Contains("Queen Links", body);
        Assert.Contains("Official", body);
        Assert.Contains("Queen Online", body);
        Assert.Contains("href=\"https://www.queenonline.com/\"", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/links"), body);
        TestHtmlAssertions.AssertPageTitle(body, "QueenZone links");
    }

    [Fact]
    public async Task LinksPageHidesUnavailableLinksAndEmptyCategories()
    {
        var repository = new InMemoryLinksRepository(
        [
            new QueenLinkCategory(
                1,
                "Official",
                [
                    new QueenLink(1, "Queen Online", "https://www.queenonline.com/", "Official Queen site.", 1, true),
                    new QueenLink(2, "Missing Site", "https://missing.example.test/", "Gone.", 1, false),
                ]),
            new QueenLinkCategory(
                2,
                "Dead Category",
                [
                    new QueenLink(3, "Dead Only", "https://dead.example.test/", "Gone.", 2, false),
                ]),
        ]);
        await repository.UpsertCheckResultsAsync(
        [
            new QueenLinkCheckUpdate(2, "https://missing.example.test/", DateTime.UtcNow, false, true, 3, 404, null),
            new QueenLinkCheckUpdate(3, "https://dead.example.test/", DateTime.UtcNow, false, true, 3, 404, null),
        ]);
        var client = CreateClientWithLinks(repository);

        var body = await client.GetStringAsync("/links");

        Assert.Contains("Queen Online", body);
        Assert.DoesNotContain("Missing Site", body);
        Assert.DoesNotContain("Dead Category", body);
        Assert.DoesNotContain("Dead Only", body);
    }

    [Fact]
    public async Task LinksPageShowsEmptyMessageWhenNoLinksSurviveAvailabilityCheck()
    {
        var repository = new InMemoryLinksRepository(
        [
            new QueenLinkCategory(
                1,
                "Dead Category",
                [
                    new QueenLink(1, "Dead Only", "https://dead.example.test/", "Gone.", 1, false),
                ]),
        ]);
        await repository.UpsertCheckResultsAsync(
        [
            new QueenLinkCheckUpdate(1, "https://dead.example.test/", DateTime.UtcNow, false, true, 3, 404, null),
        ]);
        var client = CreateClientWithLinks(repository);

        var body = await client.GetStringAsync("/links");

        Assert.Contains("No checked Queen links are available yet.", body);
        Assert.DoesNotContain("Dead Only", body);
    }

    [Fact]
    public async Task LinksPageNormalizesBareLegacyUrlsAndDisplaysHost()
    {
        var client = CreateClientWithLinks(
        [
            new QueenLinkCategory(
                1,
                "Official",
                [
                    new QueenLink(1, "Queen Online", "www.queenonline.com", "Official Queen site.", 1, true),
                ]),
        ]);

        var body = await client.GetStringAsync("/links");

        Assert.Contains("href=\"https://www.queenonline.com/\"", body);
        Assert.Contains("www.queenonline.com", body);
    }

    [Fact]
    public async Task LinksPageSkipsMalformedLegacyUrls()
    {
        var client = CreateClientWithLinks(
        [
            new QueenLinkCategory(
                1,
                "Broken",
                [
                    new QueenLink(1, "Malformed", "mailto:someone@example.test", "Not a public web link.", 1, false),
                ]),
        ]);

        var body = await client.GetStringAsync("/links");

        Assert.Contains("No checked Queen links are available yet.", body);
        Assert.DoesNotContain("Malformed", body);
    }

    private HttpClient CreateClientWithLinks(
        IReadOnlyList<QueenLinkCategory> categories) =>
        CreateClientWithLinks(new InMemoryLinksRepository(categories));

    private HttpClient CreateClientWithLinks(ILinksRepository repository) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(repository);
            })).CreateClient();
}
