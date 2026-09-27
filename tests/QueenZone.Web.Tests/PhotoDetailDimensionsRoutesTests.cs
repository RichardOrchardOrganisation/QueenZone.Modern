using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class PhotoDetailDimensionsRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public PhotoDetailDimensionsRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task PhotoDetail_ShowsOriginalDimensionsWhenKnown()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/photography/brian-may/101");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("1920 x 1080", body, StringComparison.Ordinal);
        Assert.Contains("width=\"1920\"", body, StringComparison.Ordinal);
        Assert.Contains("height=\"1080\"", body, StringComparison.Ordinal);
        Assert.Contains("Submitted by QueenFan86", body, StringComparison.Ordinal);
        Assert.DoesNotContain("0 x 0", body, StringComparison.Ordinal);
        Assert.Contains("data-photo-lightbox", body, StringComparison.Ordinal);
        Assert.Contains("data-prev-href", body, StringComparison.Ordinal);
        Assert.Contains("data-next-href", body, StringComparison.Ordinal);
        Assert.Contains("draggable=\"false\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhotoDetail_ExposesNeighborHrefsForLightboxSwipe()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/photography/brian-may/102");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-photo-lightbox", body, StringComparison.Ordinal);
        Assert.Contains("data-prev-href=\"/photography/brian-may/101\"", body, StringComparison.Ordinal);
        Assert.Contains("data-next-href=\"/photography/brian-may/103\"", body, StringComparison.Ordinal);
        Assert.Contains("draggable=\"false\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhotoDetail_OmitsZeroDimensions()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/photography/brian-may/103");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("0 x 0", body, StringComparison.Ordinal);
        Assert.DoesNotContain("width=\"0\"", body, StringComparison.Ordinal);
        Assert.Contains("Red Special close-up", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Submitted by", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PhotoCategoryGrid_ShowsDimensionsInMetaWhenKnown()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/photography/brian-may");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("1920 x 1080", body, StringComparison.Ordinal);
    }
}
