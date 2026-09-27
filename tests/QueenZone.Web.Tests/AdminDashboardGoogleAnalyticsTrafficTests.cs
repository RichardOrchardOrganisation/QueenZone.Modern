using System.Net;

namespace QueenZone.Web.Tests;

public sealed class AdminDashboardGoogleAnalyticsTrafficTests : IClassFixture<WebHostVariantCache>
{
    private readonly WebHostVariantCache variants;

    public AdminDashboardGoogleAnalyticsTrafficTests(WebHostVariantCache variants)
    {
        this.variants = variants;
    }

    [Fact]
    public async Task AdminDashboard_RendersGoogleAnalyticsTraffic()
    {
        var client = variants.Get(WebHostVariants.GaTrafficAvailable).CreateAdminClient(allowAutoRedirect: true);

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Traffic", body);
        Assert.Contains("1,234", body);
        Assert.Contains("5,678", body);
        Assert.Contains("321", body);
        Assert.Contains("Daily sessions, last 30 days", body);
        Assert.Contains("Top pages this week", body);
        Assert.Contains("/news", body);
        Assert.Contains("456", body);
    }

    [Fact]
    public async Task AdminDashboard_RendersUnavailableGoogleAnalyticsState()
    {
        var client = variants.Get(WebHostVariants.GaTrafficUnavailable).CreateAdminClient(allowAutoRedirect: true);

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Traffic", body);
        Assert.Contains("Unavailable", body);
        Assert.Contains("Google Analytics traffic is unavailable.", body);
    }
}
