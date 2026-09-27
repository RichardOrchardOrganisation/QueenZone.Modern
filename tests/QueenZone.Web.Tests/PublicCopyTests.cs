using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class PublicCopyTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public PublicCopyTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task HomePageRendersUpdatedNavigationAndAboutLinkCopy()
    {
        var body = await factory.CreateClient().GetStringAsync("/");

        Assert.Contains("The core albums", body);
        Assert.Contains("Thousands of restored images", body);
        Assert.Contains("href=\"/about\">Read More</a>", body);
        Assert.Contains("href=\"/mobile-apps\"", body);
        Assert.Contains("Try out the Mobile Apps", body);
        Assert.DoesNotContain("Tens of thousands of restored images", body);
        Assert.DoesNotContain("Explore the timeline", body);
    }
}
