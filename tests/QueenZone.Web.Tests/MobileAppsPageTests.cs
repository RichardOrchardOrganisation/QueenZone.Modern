namespace QueenZone.Web.Tests;

public sealed class MobileAppsPageTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public MobileAppsPageTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task MobileAppsPageRendersPublicAndroidAndIosStoreListings()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/mobile-apps");

        Assert.Contains(TestSiteConfiguration.CanonicalLink("/mobile-apps"), body);
        Assert.Contains("href=\"https://play.google.com/store/apps/details?id=org.queenzone.mobile\"", body);
        Assert.Contains("Get it on Google Play", body);
        Assert.Contains("The Android app is live on Google Play.", body);
        Assert.DoesNotContain("Android testing", body);
        Assert.DoesNotContain("Testing now", body);
        Assert.DoesNotContain("groups.google.com/g/queenzone-mobile", body);
        Assert.DoesNotContain("play.google.com/apps/testing", body);
        Assert.DoesNotContain("Become a tester", body);
        Assert.Contains("https://apps.apple.com/au/app/queenzone-org/id6803889011", body);
        Assert.Contains("Get it on the App Store", body);
        Assert.Contains("The iOS app is live on the App Store.", body);
        TestHtmlAssertions.AssertPageTitle(body, "Queenzone Mobile Apps");
        Assert.Contains("QueenZone on your phone", body);
        Assert.True(body.IndexOf("Get it on the App Store", StringComparison.Ordinal) < body.IndexOf("Get it on Google Play", StringComparison.Ordinal));
        Assert.DoesNotContain("Help test Queenzone", body);
        Assert.Contains("More than 200 fan performances", body);
        Assert.DoesNotContain("That is all.", body);
        Assert.DoesNotContain("TestFlight", body);
        Assert.DoesNotContain("https://apps.apple.com/app/testflight/id899247664", body);
        Assert.DoesNotContain("mailto:support@queenzone.org", body);
    }
}
