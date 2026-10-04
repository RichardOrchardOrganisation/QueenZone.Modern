using Deque.AxeCore.Playwright;
using Deque.AxeCore.Commons;
using Microsoft.Playwright;
using QueenZone.Data;

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
    [Test]
    public async Task Populated_phone_leaderboard_is_accessible_and_does_not_overflow()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-Member-Id"] = Guid.NewGuid().ToString() });
        await Page.GotoAsync("/crosswords/live-aid");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        var letters = string.Concat(CrosswordSampleData.Load().Single(puzzle => puzzle.Slug == "live-aid").Grid.Rows);
        var status = await Page.EvaluateAsync<int>("""
            async letters => {
                const config = JSON.parse(document.querySelector('[data-puzzle]').textContent);
                const token = document.querySelector('[data-antiforgery] input').value;
                const response = await fetch(location.pathname+'?handler=Complete', { method:'POST',
                  headers:{'Content-Type':'application/json','RequestVerificationToken':token,'X-Crossword-Member':config.memberId},
                  body:JSON.stringify({letters,elapsedSeconds:120,revealedCells:[],autoCheckUsed:false,updatedAt:new Date().toISOString(),playVersion:config.puzzle.playVersion}) });
                return response.status;
            }
            """, letters);
        Assert.That(status, Is.EqualTo(200));
        await Page.GotoAsync("/crosswords/live-aid/leaderboard");
        await Expect(Page.GetByText("Your rank: 1 of 1", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.Locator("tbody tr")).ToHaveCountAsync(1);
        Assert.That(await Page.Locator("#main-content").EvaluateAsync<int>("element => element.scrollWidth - element.clientWidth"), Is.LessThanOrEqualTo(1));
        var axe = await Page.Locator("#main-content").RunAxe(new AxeRunOptions {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] }
        });
        Assert.That(axe.Violations, Is.Empty);
    }

}
