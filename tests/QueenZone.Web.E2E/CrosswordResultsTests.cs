using Deque.AxeCore.Playwright;
using Deque.AxeCore.Commons;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public sealed class CrosswordResultsTests : E2EPageTest
{
    [TestCase(390)]
    [TestCase(1280)]
    public async Task Empty_leaderboard_is_accessible_and_history_requires_member(int width)
    {
        await Page.SetViewportSizeAsync(width, 844);
        await Page.GotoAsync("/crosswords/meet-the-band/leaderboard");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Meet the Band leaderboard" })).ToBeVisibleAsync();
        await Expect(Page.GetByText("No ranked solves yet. Be the first!")).ToBeVisibleAsync();
        var axe = await Page.Locator("#main-content").RunAxe(new AxeRunOptions {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] }
        });
        Assert.That(axe.Violations, Is.Empty);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Back to crossword" }).ClickAsync();
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.GotoAsync("/account/crosswords");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/account/login"));
    }
}
