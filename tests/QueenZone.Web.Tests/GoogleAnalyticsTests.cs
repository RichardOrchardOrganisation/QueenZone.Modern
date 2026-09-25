using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class GoogleAnalyticsTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private const string MeasurementId = "G-V2W56BZ3KZ";
    private readonly WebApplicationFactory<Program> factory;

    public GoogleAnalyticsTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
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

    private HttpClient CreateClientWithMeasurementId() =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{AnalyticsOptions.SectionName}:MeasurementId"] = MeasurementId
                });
            })).CreateClient();
}
