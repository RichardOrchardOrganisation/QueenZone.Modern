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

    [Test]
    public async Task MobileApps_FromHomepageShowsIosDownloadBeforeAndroidTesting()
    {
        await Page.GotoAsync("/");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Explore the mobile apps" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "QueenZone on your phone", Level = 1 })).ToBeVisibleAsync();
        var platforms = Page.Locator("article.qz-mobile-platform");
        await Expect(platforms.Nth(0).GetByRole(AriaRole.Heading, new() { Name = "iOS", Exact = true })).ToBeVisibleAsync();
        await Expect(platforms.Nth(0).GetByRole(AriaRole.Link, new() { Name = "Get it on the App Store" })).ToHaveAttributeAsync("href", "https://apps.apple.com/au/app/queenzone-org/id6803889011");
        await Expect(platforms.Nth(1).GetByRole(AriaRole.Heading, new() { Name = "Android", Exact = true })).ToBeVisibleAsync();
        await Expect(platforms.Nth(1).GetByRole(AriaRole.Link, new() { Name = "Open the testing page" })).ToHaveAttributeAsync("href", "https://play.google.com/apps/testing/org.queenzone.mobile");
        await AxeAssertions.AssertNoBlockingViolationsAsync(Page, "/mobile-apps");
    }
}
