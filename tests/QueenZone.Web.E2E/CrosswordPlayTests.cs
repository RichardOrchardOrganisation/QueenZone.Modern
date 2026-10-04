using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using QueenZone.Data;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public sealed class CrosswordPlayTests : E2EPageTest
{
    [Test]
    public async Task Installed_solver_modules_refresh_online_and_remain_available_offline()
    {
        await Page.GotoAsync("/crosswords/meet-the-band");
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.WaitForFunctionAsync("navigator.serviceWorker.controller !== null");
        await Page.EvaluateAsync("""
            async () => {
                const cache = await caches.open('qz-static-v2');
                for (const module of ['core', 'account'])
                    await cache.put(`/js/crossword-${module}.js`, new Response('obsolete solver module', { headers: { 'Content-Type': 'application/javascript' } }));
            }
            """);
        foreach (var module in new[] { "core", "account" })
        {
            var refreshed = await Page.EvaluateAsync<string>("async module => (await fetch(`/js/crossword-${module}.js`)).text()", module);
            Assert.That(refreshed, Does.Contain("export").And.Not.Contain("obsolete solver module"));
            var cached = await Page.EvaluateAsync<string>("async module => (await caches.match(`/js/crossword-${module}.js`)).text()", module);
            Assert.That(cached, Is.EqualTo(refreshed));
        }
        await Context.SetOfflineAsync(true);
        foreach (var module in new[] { "core", "account" })
        {
            var cached = await Page.EvaluateAsync<string>("async module => (await fetch(`/js/crossword-${module}.js`)).text()", module);
            Assert.That(cached, Does.Contain("export").And.Not.Contain("obsolete solver module"));
        }
        await Context.SetOfflineAsync(false);
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await Page.Locator("[data-clue='3-across']").ClickAsync();
        await Page.Keyboard.TypeAsync("ROG");
        await Expect(Page.Locator("[data-cell='9'] [data-letter]")).ToHaveTextAsync("G");
    }

    [Test]
    public async Task Mobile_keyboard_viewport_keeps_the_input_anchor_and_selected_letter_visible()
    {
        var options = new BrowserNewContextOptions(Playwright.Devices["iPhone 13"])
        { BaseURL = BaseUrl, ViewportSize = new() { Width = 390, Height = 844 } };
        if (BrowserType.Name == "firefox") options.IsMobile = false;
        var context = await CreateExtraContextAsync(options);
        await context.AddInitScriptAsync("""
            const keyboardViewport = Object.assign(new EventTarget(), { height: 844, width: 390, offsetTop: 0, offsetLeft: 0 });
            Object.defineProperty(window, 'visualViewport', { value: keyboardViewport });
            window.crosswordKeyboardViewport = keyboardViewport;
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/crosswords/meet-the-band");
        await Expect(page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await page.Locator("[data-clue='3-across']").ClickAsync();
        await page.EvaluateAsync("""
            () => {
                window.crosswordKeyboardViewport.height = 360;
                window.crosswordKeyboardViewport.offsetTop = 110;
                window.scrollBy(0, 240);
                window.crosswordKeyboardViewport.dispatchEvent(new Event('resize'));
                window.crosswordKeyboardViewport.dispatchEvent(new Event('scroll'));
            }
            """);
        await page.WaitForFunctionAsync("() => document.querySelector('[data-crossword]').classList.contains('keyboard-open')");
        await page.Locator("[data-input]").DispatchEventAsync("keydown", new { key = " ", code = "Space", keyCode = 32 });
        await page.Locator("[data-input]").FillAsync("ROG");
        await Expect(page.Locator("[data-active-clue]")).ToContainTextAsync("3 across:");
        await page.WaitForFunctionAsync("""
            () => {
                const cell = document.querySelector('[data-cell].is-selected').getBoundingClientRect();
                const clue = document.querySelector('[data-active-clue]').getBoundingClientRect();
                const input = document.querySelector('[data-input]').getBoundingClientRect();
                return document.querySelector('[data-crossword]').classList.contains('keyboard-open') &&
                    cell.top >= 110 && cell.bottom <= clue.top && clue.bottom <= 470 &&
                    Math.abs(input.top - cell.top) <= 1 && Math.abs(input.left - cell.left) <= 1;
            }
            """, null, new() { Timeout = 5000 });
        await Expect(page.Locator("[data-cell='9'] [data-letter]")).ToHaveTextAsync("G");
    }

    [Test]
    public async Task Mobile_soft_keyboard_space_keeps_the_selected_clue_direction_and_backspace()
    {
        var options = new BrowserNewContextOptions(Playwright.Devices["iPhone 13"]) { BaseURL = BaseUrl };
        if (BrowserType.Name == "firefox") options.IsMobile = false;
        var context = await CreateExtraContextAsync(options);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ColorScheme = ColorScheme.Dark });
        await page.GotoAsync("/crosswords/meet-the-band");
        await Expect(page.Locator("[data-toolbar]")).ToBeVisibleAsync();
        await page.Locator("[data-clue='3-across']").ClickAsync();
        // Mobile IMEs can emit a space without a physical Space key.
        await page.Locator("[data-input]").DispatchEventAsync("keydown", new { key = " ", code = "Unidentified" });
        await page.Locator("[data-input]").FillAsync("ROG");
        await Expect(page.Locator("[data-active-clue]")).ToContainTextAsync("3 across:");
        await Expect(page.Locator("[data-cell='7'] [data-letter]")).ToHaveTextAsync("R");
        await Expect(page.Locator("[data-cell='8'] [data-letter]")).ToHaveTextAsync("O");
        await Expect(page.Locator("[data-cell='9'] [data-letter]")).ToHaveTextAsync("G");
        await page.Locator("[data-clue='2-down']").ClickAsync();
        await page.Locator("[data-input]").DispatchEventAsync("keydown", new { key = " ", code = "Unidentified" });
        await page.Locator("[data-input]").FillAsync("MER");
        await Expect(page.Locator("[data-active-clue]")).ToContainTextAsync("2 down:");
        await Expect(page.Locator("[data-cell='4'] [data-letter]")).ToHaveTextAsync("M");
        await Expect(page.Locator("[data-cell='11'] [data-letter]")).ToHaveTextAsync("E");
        await Expect(page.Locator("[data-cell='18'] [data-letter]")).ToHaveTextAsync("R");
        await page.Locator("[data-input]").EvaluateAsync("input => input.dispatchEvent(new InputEvent('beforeinput', { inputType: 'deleteContentBackward', bubbles: true, cancelable: true }))");
        await Expect(page.Locator("[data-cell='18'] [data-letter]")).ToHaveTextAsync("");
        await page.Locator("[data-input]").FillAsync("R");
        await Expect(page.Locator("[data-cell='18'] [data-letter]")).ToHaveTextAsync("R");
        await page.Locator("[data-cell='11']").ClickAsync();
        await Expect(page.Locator("[data-active-clue]")).ToContainTextAsync("2 down:");
        await page.Locator("[data-cell='11']").ClickAsync();
        await Expect(page.Locator("[data-active-clue]")).ToContainTextAsync("3 across:");
        await StrictFeatureAccessibilityAsync(page);
    }

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
        await Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>());
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("");
        await Context.SetOfflineAsync(true);
        await Page.ReloadAsync();
        await Expect(Page.Locator("[data-cell='0'] [data-letter]")).ToHaveTextAsync("");
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
