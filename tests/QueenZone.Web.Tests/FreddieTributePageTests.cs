using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class FreddieTributePageTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public FreddieTributePageTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
        });
    }

    [Fact]
    public async Task FreddieTributePage_RendersSeedTributesPhotosAndMenuLink()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/freddie-mercury-tribute");

        Assert.Contains("Freddie Mercury Tribute", body);
        Assert.Contains("Freddie Tribute", body);
        Assert.Contains("href=\"/freddie-mercury-tribute\"", body);
        Assert.Contains("Thank you for Bohemian Rhapsody", body);
        Assert.Contains("Featured tribute", body);
        Assert.Contains("Selected Freddie Mercury photographs", body);
        Assert.Contains("https://cdn.queenzone.org/freddie-mercury/", body);
        Assert.Contains("Page 1 of 2", body);
        Assert.Contains("href=\"/freddie-mercury-tribute/page/2\"", body);
    }

    [Fact]
    public async Task FreddieTributePageTwo_RendersRemainingSeedTribute()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/freddie-mercury-tribute/page/2");

        Assert.Contains("Page 2 of 2", body);
        Assert.Contains("A beautiful voice, a brilliant writer", body);
        Assert.Contains("rel=\"prev\" href=\"/freddie-mercury-tribute\"", body);
    }

    [Fact]
    public async Task FreddieTributePageBeyondArchive_ReturnsNotFound()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/freddie-mercury-tribute/page/3");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FreddieTributePage_WithoutFreddieCategory_OmitsPhotoGallery()
    {
        await using var customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<SharedPhotoStore>();
                services.RemoveAll<IPhotoRepository>();
                services.AddSingleton(_ => new SharedPhotoStore(
                [
                    new PhotoCategorySeed(9, "Brian May",
                    [
                        new PhotoItemSeed(101, "Brian", "/Brian_May/img-101.jpg", "/Brian_May/img-101-t.jpg", new DateTime(1986, 7, 12)),
                    ]),
                ]));
                services.AddSingleton<IPhotoRepository, InMemoryPhotoRepository>();
            });
        });
        var client = customFactory.CreateClient();

        var body = await client.GetStringAsync("/freddie-mercury-tribute");

        Assert.Contains("Freddie Mercury Tribute", body);
        Assert.DoesNotContain("Selected Freddie Mercury photographs", body);
        Assert.DoesNotContain("https://cdn.queenzone.org/freddie-mercury/", body);
    }

    [Fact]
    public async Task FreddieTributePage_WithoutTributes_RendersEmptyState()
    {
        await using var customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<SharedFreddieTributeStore>();
                services.RemoveAll<IFreddieTributeRepository>();
                services.AddSingleton(_ => new SharedFreddieTributeStore([]));
                services.AddSingleton<IFreddieTributeRepository, InMemoryFreddieTributeRepository>();
            });
        });
        var client = customFactory.CreateClient();

        var body = await client.GetStringAsync("/freddie-mercury-tribute");

        Assert.Contains("No Freddie Mercury tributes are available yet.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Featured tribute", body, StringComparison.Ordinal);
    }
}
