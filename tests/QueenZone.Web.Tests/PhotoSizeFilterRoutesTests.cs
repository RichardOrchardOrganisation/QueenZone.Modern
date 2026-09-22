using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class PhotoSizeFilterRoutesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public PhotoSizeFilterRoutesTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
    }

    [Fact]
    public async Task Category_WithDesktopFilter_ShowsMatchingPhotosAndQueryOnDetailLinks()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/photography/brian-may?size=desktop");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Desktop wallpaper", body, StringComparison.Ordinal);
        Assert.Contains("matching Desktop wallpaper", body, StringComparison.Ordinal);
        // Seed: pic 101 is 1920x1080 desktop; 102 is 1600x1200 (not desktop); 103 is zero dims.
        Assert.Contains("/photography/brian-may/101?size=desktop", body, StringComparison.Ordinal);
        Assert.DoesNotContain("/photography/brian-may/102", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Category_WithImpossibleFilter_ShowsEmptyState()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // Queen category max dims in sample don't include phone portrait 1920 — 202 is 1080x1920 phone.
        // Use portrait on brian-may: only landscape-ish samples except none match phone.
        var response = await client.GetAsync("/photography/brian-may?size=phone");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("No images match", body, StringComparison.Ordinal);
        Assert.Contains("Show all sizes", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_WithSizeQuery_KeepsFilterOnNeighborsAndBackLink()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/photography/queen/201?size=desktop");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // 201 is 2560x1440 desktop; other Queen desktop candidates limited.
        Assert.Contains("Back to Queen", body, StringComparison.Ordinal);
        Assert.Contains("size=desktop", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_WhenSizeFilterExcludesPhoto_UsesUnfilteredNeighborsOnce()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/photography/brian-may/103?size=desktop");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Red Special close-up", body, StringComparison.Ordinal);
        Assert.Contains("href=\"/photography/brian-may/102\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("size=desktop", body, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotoRoutes_AppendSizeQuery()
    {
        Assert.Equal("/photography/queen", PhotoRoutes.GetCategoryPath("queen"));
        Assert.Equal(
            "/photography/queen?size=desktop",
            PhotoRoutes.GetCategoryPath("queen", new PhotoListFilter(PhotoSizePreset.Desktop)));
        Assert.Equal(
            "/photography/queen/101?size=phone",
            PhotoRoutes.GetDetailPath("queen", 101, new PhotoListFilter(PhotoSizePreset.Phone)));
    }
}
