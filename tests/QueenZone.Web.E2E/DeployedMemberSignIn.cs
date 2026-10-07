using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Shared password sign-in against the deployed DEV origin.
/// Used by the DeployedAuth cookie matrix and the DevJourney tip gate.
/// Callers must not start Playwright tracing or record video around this form.
/// </summary>
internal static class DeployedMemberSignIn
{
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
}
