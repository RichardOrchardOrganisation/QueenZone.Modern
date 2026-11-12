using Microsoft.Playwright;
using QueenZone.Data;

namespace QueenZone.Web.E2E;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class AccountAuthenticationTests : E2EPageTest
{
    [Test]
    public async Task PasswordSignInRejectsInvalidPasswordThenOpensSettings()
    {
        await Page.GotoAsync("/account/login?returnUrl=%2Faccount%2Fsettings");
        await Page.GetByText("Other ways to sign in", new() { Exact = true }).ClickAsync();
        await Page.GetByLabel("Email", new() { Exact = true }).FillAsync(BrowserFixtureMembers.AuthenticationOwner.Email);
        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync("wrong-password");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Page.GetByText("Other ways to sign in", new() { Exact = true }).ClickAsync();
        await Expect(Page.GetByText("Incorrect email or password.", new() { Exact = true })).ToBeVisibleAsync();

        await Page.GetByLabel("Password", new() { Exact = true }).FillAsync(BrowserFixtureMembers.AuthenticationPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(BaseUrl + "/account/settings");
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Textbox, new() { Name = "Display name" })).ToHaveValueAsync(BrowserFixtureMembers.AuthenticationOwner.DisplayName);
    }

    [TestCase("/account/external-login-callback", "/account/login")]
    [TestCase("/account/link-external-login", "/account/login?externalLink=expired")]
    public async Task MissingExternalStateReturnsToSignIn(string route, string destination)
    {
        await Page.GotoAsync(route);
        await Expect(Page).ToHaveURLAsync(BaseUrl + destination);
        await Expect(Page.GetByText("Other ways to sign in", new() { Exact = true })).ToBeVisibleAsync();
    }
}
