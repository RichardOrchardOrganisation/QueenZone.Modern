using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Shared password sign-in against the deployed DEV origin.
/// Used by the DeployedAuth cookie matrix and the DevJourney tip gate.
/// Callers must not start Playwright tracing or record video around this form.
/// </summary>
internal static class DeployedMemberSignIn
{
    /// <summary>
    /// ASP.NET Core cookie name for <c>MembersCookie</c> (and chunked
    /// <c>…C1</c> suffixes). Matches
    /// <c>AdminAuthenticationSchemes.MemberCookieName</c> without referencing
    /// the web project.
    /// </summary>
    internal const string MemberSessionCookieName = ".AspNetCore.MembersCookie";

    internal const string SignedOutMessage =
        "DevJourney signed in once at fixture setup. This test's browser context has no member session; refusing to sign in again.";

    internal static string RequirePassword()
    {
        var password = Environment.GetEnvironmentVariable("DEV_AUTH_E2E_PASSWORD");
        Assert.That(
            password,
            Is.Not.Null.And.Not.Empty,
            "DEV_AUTH_E2E_PASSWORD must come from the dev snapshot member secret.");
        return password!;
    }

    internal static async Task FillPasswordFormAndSubmitAsync(IPage page)
    {
        var password = RequirePassword();
        await page.GetByText("Other ways to sign in").ClickAsync();
        await page.GetByLabel("Email").FillAsync(DeployedAuthTarget.MemberEmail);
        await page.GetByLabel("Password").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();
    }

    internal static async Task SignInAsync(IPage page)
    {
        await page.GotoAsync("/account/settings");
        await FillPasswordFormAndSubmitAsync(page);
    }

    /// <summary>
    /// Sign in once in a dedicated browser, assert signed-in chrome, and return
    /// Playwright storage state JSON for later test contexts. Does not start
    /// tracing or video.
    /// </summary>
    internal static async Task<string> CaptureSignedInStorageStateAsync(string? baseUrl)
    {
        var origin = DeployedAuthTarget.RequireDevUrl(baseUrl).ToString();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = origin,
        });
        var page = await context.NewPageAsync();
        await SignInAsync(page);
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).First)
            .ToBeVisibleAsync();
        return await context.StorageStateAsync();
    }

    internal static async Task AssertContextHasMemberSessionAsync(IBrowserContext context)
    {
        AssertHasMemberSession(await context.CookiesAsync());
    }

    internal static void AssertHasMemberSession(IReadOnlyList<BrowserContextCookiesResult> cookies)
    {
        Assert.That(cookies.Any(IsMemberSessionCookie), Is.True, SignedOutMessage);
    }

    internal static bool IsMemberSessionCookie(BrowserContextCookiesResult cookie) =>
        cookie.Name.StartsWith(MemberSessionCookieName, StringComparison.Ordinal)
        && !string.IsNullOrEmpty(cookie.Value);
}
