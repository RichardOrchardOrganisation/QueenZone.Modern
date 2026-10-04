using System.Text.RegularExpressions;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public sealed class AdminCrosswordTests : E2EPageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User-Email"] = "admin@test.local" }
    };

    [Test]
    public async Task Catalogue_and_visual_builder_are_accessible_and_save_a_five_by_five_draft()
    {
        await Page.RouteAsync("**/*?handler=Validate", async route =>
        {
            var response = await route.FetchAsync();
            await Task.Delay(300); // Old snapshots can return during the next edit.
            await route.FulfillAsync(new() { Response = response });
        });
        await Page.GotoAsync("/admin/crosswords");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Crosswords", Exact = true })).ToBeVisibleAsync();
        await AssertAccessibleAsync();
        await Page.GetByRole(AriaRole.Link, new() { Name = "New crossword", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-builder]")).ToBeVisibleAsync();
        var slug = "browser-builder-" + Guid.NewGuid().ToString("N");
        await Page.GetByLabel("Title", new() { Exact = true }).FillAsync("Browser builder draft");
        await Page.GetByLabel("Slug", new() { Exact = true }).FillAsync(slug);
        await Page.GetByRole(AriaRole.Radio, new() { Name = "Letters", Exact = true }).CheckAsync();
        await Page.Locator("[data-grid] button").First.ClickAsync();
        await Page.Keyboard.TypeAsync(new string('A', 25), new() { Delay = 20 });
        await Expect(Page.Locator("[data-clues] fieldset")).ToHaveCountAsync(10);
        var clues = Page.Locator("[data-clues]").GetByLabel("Clue", new() { Exact = true });
        for (var index = 0; index < 10; index++) await clues.Nth(index).FillAsync("The first letter repeated five times");
        for (var index = 0; index < 10; index++) await Expect(clues.Nth(index)).ToHaveValueAsync("The first letter repeated five times");
        await Expect(Page.Locator("[data-validation]")).Not.ToContainTextAsync("Error:");
        await AssertAccessibleAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save draft", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/crosswords/[a-f0-9-]+/edit$"));
        await Expect(Page.GetByLabel("Slug", new() { Exact = true })).ToHaveValueAsync(slug);
        await Expect(Page.Locator("[data-grid] button")).ToHaveCountAsync(25);
        await Expect(Page.Locator("[data-clues] fieldset")).ToHaveCountAsync(10);
    }

    [Test]
    public async Task Private_preview_uses_real_solver_without_overwriting_guest_or_member_storage()
    {
        await Page.GotoAsync("/admin/crosswords?search=Meet");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Preview", Exact = true }).First.ClickAsync();
        await Expect(Page.GetByText("Preview — not published.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.EvaluateAsync("() => localStorage.setItem('qz.crossword.preview.guard','keep')");
        var before = await Page.EvaluateAsync<string>("() => JSON.stringify(Object.keys(localStorage).sort().map(key => [key,localStorage.getItem(key)]))");
        await Page.Locator("[data-cell='37']").ClickAsync(); await Page.Keyboard.TypeAsync("BRIAN");
        await Page.GetByText("Check or reveal", new() { Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Check word", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-status]")).ToContainTextAsync("correct");
        await Page.GetByLabel("Show answers", new() { Exact = true }).CheckAsync();
        await Expect(Page.Locator(".admin-crossword-answer").First).ToBeVisibleAsync();
        await Page.GetByLabel("Width preset").SelectOptionAsync("360px");
        await AssertAccessibleAsync();
        var after = await Page.EvaluateAsync<string>("() => JSON.stringify(Object.keys(localStorage).sort().map(key => [key,localStorage.getItem(key)]))");
        Assert.That(after, Is.EqualTo(before));
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-cell='37'] [data-letter]")).ToHaveTextAsync("");
    }

    private async Task AssertAccessibleAsync()
    {
        var result = await Page.Locator("#main-content").RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] }
        });
        Assert.That(result.Violations.Where(item => item.Impact is "serious" or "critical").Select(item => item.Id), Is.Empty);
    }
}
