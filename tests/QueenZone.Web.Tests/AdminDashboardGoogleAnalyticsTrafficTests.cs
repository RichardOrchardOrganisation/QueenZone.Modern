using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminDashboardGoogleAnalyticsTrafficTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public AdminDashboardGoogleAnalyticsTrafficTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AdminDashboard_RendersGoogleAnalyticsTraffic()
    {
        var client = CreateClient(new GoogleAnalyticsTrafficSnapshot(
            true,
            SessionsLast7Days: 1234,
            PageViewsLast7Days: 5678,
            ActiveUsersLast7Days: 321,
            TopPagesThisWeek:
            [
                new GoogleAnalyticsTopPage("/news", 456),
                new GoogleAnalyticsTopPage("/forum", 123),
            ],
            DailySessionsLast30Days:
            [
                new GoogleAnalyticsDailySession(DateOnly.FromDateTime(DateTime.UtcNow), 44),
            ]));

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
        var client = CreateClient(GoogleAnalyticsTrafficSnapshot.Unavailable("Google Analytics traffic is unavailable."));

        var response = await client.GetAsync("/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Traffic", body);
        Assert.Contains("Unavailable", body);
        Assert.Contains("Google Analytics traffic is unavailable.", body);
    }

    private HttpClient CreateClient(GoogleAnalyticsTrafficSnapshot snapshot)
    {
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IGoogleAnalyticsTrafficService>();
                services.AddSingleton<IGoogleAnalyticsTrafficService>(
                    new StubGoogleAnalyticsTrafficService(snapshot));
            });
        }).CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, AdminHttpTestHelpers.AdminEmail);
        return client;
    }

    private sealed class StubGoogleAnalyticsTrafficService(GoogleAnalyticsTrafficSnapshot snapshot)
        : IGoogleAnalyticsTrafficService
    {
        public Task<GoogleAnalyticsTrafficSnapshot> GetDashboardTrafficAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }
}

