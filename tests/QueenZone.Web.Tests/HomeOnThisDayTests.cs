using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class HomeOnThisDayTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public HomeOnThisDayTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task HomePageRendersOnThisDayMatchesForFixedDate()
    {
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero)));
            });
        }).CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("This Day in Queen History", body);
        Assert.Contains("Queen&#x27;s Live Aid performance", body);
        Assert.Contains("Queen released", body);
        Assert.Contains("<time datetime=\"1985-07-13\">13 Jul 1985</time>", body);
        Assert.DoesNotContain("nearby dates", body);
    }

    [Fact]
    public async Task HomePageFallsBackToNearbyDatesWhenNoExactMatchExists()
    {
        var client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 7, 12, 12, 0, 0, TimeSpan.Zero)));
            });
        }).CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("nearby dates from the archive", body);
        Assert.Contains("Queen&#x27;s Live Aid performance", body);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
