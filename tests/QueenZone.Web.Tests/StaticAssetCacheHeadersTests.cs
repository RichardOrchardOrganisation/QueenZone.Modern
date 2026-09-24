using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class StaticAssetCacheHeadersTests :
    IClassFixture<ProductionWebApplicationFactory>,
    IClassFixture<DevelopmentWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> productionFactory;
    private readonly WebApplicationFactory<Program> developmentFactory;

    public StaticAssetCacheHeadersTests(
        ProductionWebApplicationFactory productionFactory,
        DevelopmentWebApplicationFactory developmentFactory)
    {
        this.productionFactory = productionFactory;
        this.developmentFactory = developmentFactory;
    }

    [Fact]
    public async Task VersionedCss_HasLongLivedImmutableCacheControlInProduction()
    {
        var client = productionFactory.CreateClient();

        var response = await client.GetAsync("/css/site.css?v=test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StaticFileCacheControl.VersionedCacheHeader, response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task VersionedJs_HasLongLivedImmutableCacheControlInProduction()
    {
        var client = productionFactory.CreateClient();

        var response = await client.GetAsync("/js/site.js?v=test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StaticFileCacheControl.VersionedCacheHeader, response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task VersionedHomeArchiveHeroJs_HasLongLivedImmutableCacheControlInProduction()
    {
        var client = productionFactory.CreateClient();

        var response = await client.GetAsync("/js/home-archive-hero.js?v=test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StaticFileCacheControl.VersionedCacheHeader, response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task UnversionedImage_HasShortPublicCacheControlInProduction()
    {
        var client = productionFactory.CreateClient();

        var response = await client.GetAsync("/design-system/assets/crest-white.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StaticFileCacheControl.UnversionedCacheHeader, response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task UnversionedCss_DoesNotUseImmutableCacheControlInProduction()
    {
        var client = productionFactory.CreateClient();

        var response = await client.GetAsync("/css/site.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(StaticFileCacheControl.UnversionedCacheHeader, response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain("immutable", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VersionedCss_DoesNotSetProductionCacheHeadersInDevelopment()
    {
        var client = developmentFactory.CreateClient();

        var response = await client.GetAsync("/css/site.css?v=test");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.CacheControl);
    }

    [Fact]
    public async Task WellKnownFile_RemainsServedWithoutImmutableCacheHeader()
    {
        var client = productionFactory.CreateClient();

        var response = await client.GetAsync("/.well-known/microsoft-identity-association.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(StaticFileCacheControl.VersionedCacheHeader, response.Headers.CacheControl?.ToString());
        Assert.DoesNotContain("immutable", response.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }
}
