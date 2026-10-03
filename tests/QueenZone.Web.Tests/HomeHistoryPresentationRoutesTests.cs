using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class HomeHistoryPresentationRoutesTests(QueenZoneWebApplicationFactory factory)
    : IClassFixture<QueenZoneWebApplicationFactory>
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Home_PreservesEmptyExactAndNearbyHistoryStates(int state)
    {
        IReadOnlyList<QueenHistoryEvent> events = state == 0 ? [] :
        [
            WebHostVariants.TimelineEvent(1987001, "Presentation history event", new DateTime(1977, 10, state == 1 ? 2 : 3)),
        ];
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IQueenHistoryRepository>();
            services.AddSingleton<IQueenHistoryRepository>(new InMemoryQueenHistoryRepository(events));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new HistoryTimeProvider());
        }));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("This Day in Queen History", html);
        if (state == 0)
        {
            Assert.Contains("No exact Queen history records are available for today yet.", html);
            Assert.DoesNotContain("Presentation history event", html);
        }
        else
        {
            Assert.Contains("Presentation history event", html);
            Assert.DoesNotContain("No exact Queen history records are available for today yet.", html);
        }
        const string nearby = "No exact match for today yet. Showing nearby dates from the archive.";
        Assert.Equal(state == 2, html.Contains(nearby, StringComparison.Ordinal));
    }

    private sealed class HistoryTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    }
}
