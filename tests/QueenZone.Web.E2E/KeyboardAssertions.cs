using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Shared skip-link keyboard check for curated PR-gate pages (#1597).
/// </summary>
internal static class KeyboardAssertions
{
    public static async Task AssertSkipLinkMovesFocusToMainAsync(IPage page)
    {
        var skipLink = page.GetByRole(AriaRole.Link, new() { Name = "Skip to content" });
        await Assertions.Expect(skipLink).ToBeAttachedAsync();

        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(skipLink).ToBeFocusedAsync();

        await skipLink.PressAsync("Enter");

        var main = page.GetByRole(AriaRole.Main);
        await Assertions.Expect(main).ToHaveAttributeAsync("id", "main-content");
        await Assertions.Expect(main).ToBeFocusedAsync();
    }
}
