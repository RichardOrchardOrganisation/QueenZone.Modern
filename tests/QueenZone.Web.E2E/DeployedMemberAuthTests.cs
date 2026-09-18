using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace QueenZone.Web.E2E;

/// <summary>
/// Exercises the real member password and cookie flow on the deployed dev app.
/// This fixture intentionally does not use E2EPageTest: failure traces can contain
/// the password form payload and must not be uploaded as CI artifacts.
/// </summary>
[TestFixture]
[Category(E2ECategories.DeployedAuth)]
public class DeployedMemberAuthTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = DeployedAuthTarget.RequireDevUrl(Environment.GetEnvironmentVariable("E2E_BASE_URL")).ToString(),
    };

    [Test]
    public async Task SyntheticMemberCanSignInKeepSessionAndSignOut()
    {
        var password = Environment.GetEnvironmentVariable("DEV_AUTH_E2E_PASSWORD");
        Assert.That(password, Is.Not.Null.And.Not.Empty,
            "DEV_AUTH_E2E_PASSWORD must come from the dev snapshot member secret.");

        await Page.GotoAsync("/account/settings");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/account/login\\?ReturnUrl=.*"));

        await Page.GetByText("Other ways to sign in").ClickAsync();
        await Page.GetByLabel("Email").FillAsync(DeployedAuthTarget.MemberEmail);
        await Page.GetByLabel("Password").FillAsync(password!);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync("https://dev.queenzone.org/account/settings");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Account settings", Level = 1 }))
            .ToBeVisibleAsync();
        await Expect(Page.Locator(".qz-account-settings__meta")).ToContainTextAsync(DeployedAuthTarget.MemberEmail);

        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Account settings", Level = 1 }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).First)
            .ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).First.ClickAsync();
        await Expect(Page).ToHaveURLAsync("https://dev.queenzone.org/account/login?signedOut=1");
        await Expect(Page.GetByText("You have been signed out of QueenZone.")).ToBeVisibleAsync();

        await Page.GotoAsync("/account/settings");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(".*/account/login\\?ReturnUrl=.*"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Account settings", Level = 1 }))
            .ToHaveCountAsync(0);
    }
}
