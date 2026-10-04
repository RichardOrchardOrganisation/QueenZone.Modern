using Microsoft.Playwright;
using QueenZone.Data;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public sealed class CrosswordPlayTests : E2EPageTest
{
    [Test]
    public async Task List_and_play_have_no_blocking_accessibility_violations()
    {
        await Page.GotoAsync("/crosswords");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Queen crosswords", Exact = true })).ToBeVisibleAsync();
        await AxeAssertions.AssertNoBlockingViolationsAsync(Page, "/crosswords");
        await StrictFeatureAccessibilityAsync(Page);
        await Page.GotoAsync("/crosswords/meet-the-band");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.SetViewportSizeAsync(1280, 800);
        await AxeAssertions.AssertNoBlockingViolationsAsync(Page, "/crosswords/meet-the-band");
        await StrictFeatureAccessibilityAsync(Page);
        await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(E2EArtifactPaths.EnsureDirectory(), "crossword-desktop.png"), FullPage = true });
    }

    [Test]
    public async Task Keyboard_navigation_check_and_pause_keep_grid_state()
    {
        await Page.GotoAsync("/crosswords/meet-the-band");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.Locator("[data-cell='7']").ClickAsync();
        await Page.Keyboard.TypeAsync("Z");
        await Expect(Page.Locator("[data-cell='7'] [data-letter]")).ToHaveTextAsync("Z");
        await Page.Locator("[data-cell='7']").ClickAsync();
        await Page.Keyboard.PressAsync("Space");
        await Expect(Page.Locator("[data-active-clue]")).ToContainTextAsync("across:");
        await Page.Keyboard.PressAsync("ArrowDown");
        await Expect(Page.Locator("[data-active-clue]")).ToContainTextAsync("down:");
        await Page.Keyboard.PressAsync("Tab");
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Page.Keyboard.PressAsync("Control+Enter");
        await Expect(Page.Locator("[data-status]")).ToContainTextAsync("incorrect");
        await Page.Keyboard.PressAsync("Backspace");
        await Page.Locator("[data-pause]").ClickAsync();
        await Expect(Page.Locator("[data-grid-scroll]")).ToBeHiddenAsync();
        await Expect(Page.Locator("[data-paused]")).ToBeVisibleAsync();
        await Page.Locator("[data-resume]").ClickAsync();
        await Expect(Page.Locator("[data-grid-scroll]")).ToBeVisibleAsync();
        await Page.Locator("[data-menu] summary").ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Page.Locator("[data-menu]")).Not.ToHaveAttributeAsync("open", "");
    }

    [Test]
    public async Task Reveal_confirmation_markers_and_local_reload_preserve_assistance()
    {
        await Page.GotoAsync("/crosswords/meet-the-band");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.Locator("[data-menu] summary").ClickAsync();
        EventHandler<IDialog> decline = async (_, dialog) => await dialog.DismissAsync();
        Page.Dialog += decline;
        await Page.Locator("[data-reveal=entry]").ClickAsync();
        await Expect(Page.Locator("[data-marker]").Filter(new() { HasText = "▲" })).ToHaveCountAsync(0);
        Page.Dialog -= decline;
        EventHandler<IDialog> accept = async (_, dialog) => await dialog.AcceptAsync();
        Page.Dialog += accept;
        await Page.Locator("[data-reveal=entry]").ClickAsync();
        await Expect(Page.Locator("[data-marker]").Filter(new() { HasText = "▲" }).First).ToBeVisibleAsync();
        await StrictFeatureAccessibilityAsync(Page);
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-marker]").Filter(new() { HasText = "▲" }).First).ToBeVisibleAsync();
        Page.Dialog -= accept;
    }

    [TestCase("iPhone 13")]
    [TestCase("Pixel 7")]
    public async Task Mobile_browser_types_checks_and_completes_a_seed(string device)
    {
        var options = new BrowserNewContextOptions(Playwright.Devices[device]) { BaseURL = BaseUrl };
        if (BrowserType.Name == "firefox") options.IsMobile = false;
        var context = await CreateExtraContextAsync(options);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/crosswords/meet-the-band");
        await Expect(page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(E2EArtifactPaths.EnsureDirectory(), "crossword-" + device.Replace(" ", "") + ".png"), FullPage = true });
        var seed = CrosswordSampleData.Load().Single(puzzle => puzzle.Slug == "meet-the-band");
        var letters = string.Concat(seed.Grid.Rows);
        for (var cell = 0; cell < letters.Length; cell++)
        {
            if (letters[cell] == '#') continue;
            await page.Locator($"[data-cell='{cell}']").ClickAsync();
            await page.Locator("[data-input]").FillAsync(letters[cell].ToString());
        }
        await Expect(page.Locator("[data-completion]")).ToBeVisibleAsync();
        await Expect(page.Locator("[data-completion-message]")).ToContainTextAsync("Clean solve");
    }

    [Test]
    public async Task No_javascript_and_print_keep_a_blank_grid_and_clues()
    {
        var seed = CrosswordSampleData.Load().First(puzzle => puzzle.Grid.Width == 13);
        var context = await CreateExtraContextAsync(new() { BaseURL = BaseUrl, JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/crosswords/" + seed.Slug);
        await Expect(page.Locator("noscript")).ToBeVisibleAsync();
        Assert.That(await page.Locator("noscript").TextContentAsync(), Does.Contain("Turn on JavaScript to enter letters."));
        await Expect(page.Locator("[role=grid]")).ToBeVisibleAsync();
        Assert.That(await page.Locator("[role=grid]").EvaluateAsync<double>("element => element.getBoundingClientRect().width"), Is.GreaterThan(500));
        await page.EmulateMediaAsync(new() { Media = Media.Print, ColorScheme = ColorScheme.Dark });
        await Expect(page.Locator("[data-letter]").First).ToHaveTextAsync("");
        await Expect(page.Locator("[data-print]")).ToBeHiddenAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Across", Exact = true })).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(E2EArtifactPaths.EnsureDirectory(), "crossword-print-13.png"), FullPage = true });
        if (BrowserType.Name == "chromium")
        {
            foreach (var format in new[] { "A4", "Letter" })
                await page.PdfAsync(new() { Path = System.IO.Path.Combine(E2EArtifactPaths.EnsureDirectory(), $"crossword-print-13-{format}.pdf"), Format = format, PrintBackground = true });
        }
    }

    [Test]
    public async Task Installed_shell_reloads_offline_with_local_letters_and_connection_only_actions()
    {
        if (BrowserType.Name == "webkit")
            Assert.Ignore("Playwright WebKit fails internally on service-worker offline reload. Installed desktop PWA proof runs in Chromium; real Safari offline remains manual.");
        await Page.GotoAsync("/crosswords/meet-the-band");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("navigator.serviceWorker.controller !== null");
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("async () => { const response = await caches.match(location.href); return response?.headers.get('X-QueenZone-Crossword-Shell') === 'public'; }");
        Assert.That(await Page.EvaluateAsync<bool>("async () => { const html = await (await caches.match(location.href)).text(); return html.includes('__RequestVerificationToken'); }"), Is.False);
        await Page.Locator("[data-cell='0']").ClickAsync();
        await Page.Keyboard.TypeAsync("Z");
        await Context.SetOfflineAsync(true);
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("Z");
        await Page.Locator("[data-menu] summary").ClickAsync();
        await Page.Locator("[data-menu] [data-check=grid]").ClickAsync();
        await Expect(Page.Locator("[data-status]")).ToHaveTextAsync("Needs a connection");
        await Page.Locator("[data-cell='7']").ClickAsync();
        await Page.Keyboard.TypeAsync("R");
        await Expect(Page.Locator("[data-cell='7'] [data-letter]")).ToHaveTextAsync("R");
        await Context.SetOfflineAsync(false);
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-cell='7'] [data-letter]")).ToHaveTextAsync("R");
    }

    [Test]
    public async Task Member_offline_letters_stay_partitioned_and_reconnect_syncs_only_the_current_account()
    {
        if (BrowserType.Name == "webkit")
            Assert.Ignore("Playwright WebKit fails internally on service-worker offline reload. Installed desktop PWA proof runs in Chromium; real Safari offline remains manual.");
        var member = Guid.NewGuid().ToString();
        var otherMember = Guid.NewGuid().ToString();
        await Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-Member-Id"] = member });
        await Page.GotoAsync("/crosswords/meet-the-band");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("navigator.serviceWorker.controller !== null");
        await Page.ReloadAsync();
        await Page.WaitForFunctionAsync("async () => (await caches.match(location.href))?.headers.get('X-QueenZone-Crossword-Shell') === 'public'");
        var shell = await Page.EvaluateAsync<string>("async () => (await (await caches.match(location.href)).text())");
        Assert.That(shell, Does.Not.Contain(member).And.Not.Contain("__RequestVerificationToken"));
        await Page.Locator("[data-cell='0']").ClickAsync();
        await Page.Keyboard.TypeAsync("Z");
        await Context.SetOfflineAsync(true);
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("Z");
        await Page.Locator("[data-cell='7']").ClickAsync();
        await Page.Keyboard.TypeAsync("R");
        await Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-Member-Id"] = otherMember });
        await Context.SetOfflineAsync(false);
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("");
        await Expect(Page.Locator("[data-cell='7'] [data-letter]")).ToHaveTextAsync("");
        await Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string> { ["X-Test-Member-Id"] = member });
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("Z");
        await Expect(Page.Locator("[data-cell='7'] [data-letter]")).ToHaveTextAsync("R");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dark_theme_markers_and_unavailable_device_storage_do_not_block_play()
    {
        await Page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark, ReducedMotion = ReducedMotion.Reduce });
        await Page.AddInitScriptAsync("Storage.prototype.setItem = function() { throw new DOMException('Full', 'QuotaExceededError'); }");
        await Page.GotoAsync("/crosswords/meet-the-band");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.Locator("[data-cell='0']").ClickAsync();
        await Page.Keyboard.TypeAsync("Z");
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("Z");
        await Expect(Page.Locator("[data-status]")).ToContainTextAsync("Device storage is unavailable");
        await Page.Locator("[data-menu] summary").ClickAsync();
        await Page.Locator("[data-check=grid]").First.ClickAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-marker]")).ToHaveTextAsync("×");
        await StrictFeatureAccessibilityAsync(Page);
        await Page.Locator("[data-reveal=cell]").ClickAsync();
        await Expect(Page.Locator("[data-marker]").Filter(new() { HasText = "▲" }).First).ToBeVisibleAsync();
        await StrictFeatureAccessibilityAsync(Page);
        await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(E2EArtifactPaths.EnsureDirectory(), "crossword-dark.png"), FullPage = true });
    }

    private static async Task StrictFeatureAccessibilityAsync(IPage page)
    {
        var result = await page.Locator("#main-content").RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] }
        });
        foreach (var violation in result.Violations.Where(violation => violation.Impact is "serious" or "critical"))
            TestContext.Out.WriteLine(string.Join("\n", violation.Nodes.Select(node => node.Html)));
        Assert.That(result.Violations.Where(violation => violation.Impact is "serious" or "critical").Select(violation => violation.Id),
            Is.Empty, "The new crossword content has no accessibility exceptions.");
    }
}
