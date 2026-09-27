using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class GoogleAnalyticsTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>
{
    private const string MeasurementId = WebHostVariants.AnalyticsMeasurementId;
    private readonly WebApplicationFactory<Program> factory;
    private readonly VariantWebApplicationFactory configured;

    public GoogleAnalyticsTests(QueenZoneWebApplicationFactory factory, WebHostVariantCache variants)
    {
        this.factory = factory;
        configured = variants.Get(WebHostVariants.AnalyticsMeasurementConfigured);
    }

    [Fact]
    public async Task PublicPages_OmitGoogleAnalyticsInTestingEnvironment()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum");

        Assert.DoesNotContain("googletagmanager.com/gtag/js", body);
        Assert.DoesNotContain(MeasurementId, body);
    }

    [Fact]
    public async Task PublicPages_IncludeGoogleAnalyticsWhenConfigured()
    {
        var client = CreateClientWithMeasurementId();

        var body = await client.GetStringAsync("/forum");

        // Deferred idle/load loader (not a blocking head script) still embeds the tag URL + config.
        Assert.Contains($"https://www.googletagmanager.com/gtag/js?id=", body);
        Assert.Contains(MeasurementId, body);
        Assert.Contains("requestIdleCallback", body);
        Assert.Contains("gtag('config', measurementId);", body);
        // Must not sit in <head> ahead of critical CSS/fonts.
        var headEnd = body.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        var analyticsMarker = body.IndexOf("requestIdleCallback", StringComparison.Ordinal);
        Assert.True(headEnd > 0 && analyticsMarker > headEnd, "Analytics loader should appear after </head>.");
    }

    [Fact]
    public async Task AdminPages_OmitGoogleAnalyticsEvenWhenConfigured()
    {
        var client = CreateClientWithMeasurementId();

        var response = await client.GetAsync("/Admin/News");

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("googletagmanager.com/gtag/js", body);
        Assert.DoesNotContain(MeasurementId, body);
    }

    private HttpClient CreateClientWithMeasurementId() => configured.CreateClient();
}
