namespace QueenZone.Web.Tests;

public sealed class MobileAppsPageTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public MobileAppsPageTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task MobileAppsPageRendersAndroidAndIosTestingInstructions()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/mobile-apps");

        Assert.Contains(TestSiteConfiguration.CanonicalLink("/mobile-apps"), body);
        Assert.Contains("https://groups.google.com/g/queenzone-mobile", body);
        Assert.Contains("https://play.google.com/apps/testing/org.queenzone.mobile", body);
        Assert.Contains("Join the Google Group", body);
        Assert.Contains("Join the testing group to get access to the test app", body);
        Assert.Contains("Become a tester", body);
        Assert.Contains("Open the testing page", body);
        Assert.Contains("Sometimes it takes about 5 minutes for your account to register in the Google Group", body);
        Assert.Contains("support@queenzone.org", body);
        Assert.Contains("TestFlight", body);
        Assert.Contains("More than 200 fan performances", body);
        Assert.DoesNotContain("That is all.", body);
    }
}
