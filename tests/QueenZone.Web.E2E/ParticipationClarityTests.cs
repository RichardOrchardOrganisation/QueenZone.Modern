using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class ParticipationClarityTests : E2EPageTest
{
    [Test]
    public async Task Forum_ExplainsParticipationAndLinksToSignIn()
    {
        await Page.GotoAsync("/forum");
        await Expect(Page.GetByText("Explore the original QueenZone discussions and join the conversation.", new() { Exact = false })).ToBeVisibleAsync();
        var signIn = Page.GetByRole(AriaRole.Link, new() { Name = "Sign in to participate" });
        await Expect(signIn).ToHaveAttributeAsync("href", "/account/login?returnUrl=%2Fforum");
        await AxeAssertions.AssertNoBlockingViolationsAsync(Page, "/forum");
    }

    [TestCase(1440, 900)]
    [TestCase(390, 844)]
    public async Task MobileApps_FromHomepageShowsPublicStoreDownloads(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync("/");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Download QueenZone for iPhone on the App Store" })).ToHaveAttributeAsync("href", "https://apps.apple.com/au/app/queenzone-org/id6803889011");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Download QueenZone for Android on Google Play" })).ToHaveAttributeAsync("href", "https://play.google.com/store/apps/details?id=org.queenzone.mobile");
        await PageShapeAssertions.AssertNoHorizontalOverflowAsync(Page, "homepage");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Explore the mobile apps" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "QueenZone on your phone", Level = 1 })).ToBeVisibleAsync();
        var platforms = Page.Locator("article.qz-mobile-platform");
        await Expect(platforms.Nth(0).GetByRole(AriaRole.Heading, new() { Name = "iOS", Exact = true })).ToBeVisibleAsync();
        await Expect(platforms.Nth(0).GetByRole(AriaRole.Link, new() { Name = "Get it on the App Store" })).ToHaveAttributeAsync("href", "https://apps.apple.com/au/app/queenzone-org/id6803889011");
        await Expect(platforms.Nth(1).GetByRole(AriaRole.Heading, new() { Name = "Android", Exact = true })).ToBeVisibleAsync();
        await Expect(platforms.Nth(1).GetByRole(AriaRole.Link, new() { Name = "Get it on Google Play" })).ToHaveAttributeAsync("href", "https://play.google.com/store/apps/details?id=org.queenzone.mobile");
        await Expect(Page.GetByText("The Android app is live on Google Play.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Join the Google Group" })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Open the testing page" })).ToHaveCountAsync(0);
        await PageShapeAssertions.AssertNoHorizontalOverflowAsync(Page, "/mobile-apps");
        await AxeAssertions.AssertNoBlockingViolationsAsync(Page, "/mobile-apps");
    }
}
