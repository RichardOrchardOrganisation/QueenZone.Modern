using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class ForumPageTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ForumPageTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task ForumPageRendersSeedCategories()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/forum");

        Assert.Contains("Forum", body);
        Assert.Contains("The Music", body);
        Assert.Contains("/forum/1/the-music", body);
        Assert.Contains("<strong>6</strong> boards", body);
        Assert.Contains("Ranking every studio album", body);
        Assert.Contains("/forum/topic/1002/ranking-every-studio-album", body);
        Assert.Contains("Latest activity across boards", body);
        Assert.DoesNotContain("The definitive ranking of every studio album", body);
        Assert.Contains(TestSiteConfiguration.CanonicalLink("/forum"), body);
    }
}
