using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public class ForumYoutubeVideoTests : E2EPageTest
{
    private const string Archive = "/forum/topic/1030/archive-sample-thread-1030";
    private const string LongThread = "/forum/topic/1029/archive-sample-thread-1029";
    private const string Cards = "[data-qz-forum-video]";
    private const string Stub = """
        <!doctype html><title>Stub player</title><button id="play">Play fixture</button>
        <script>
        window.commands = [];
        const parentOrigin = new URL(location.href).searchParams.get('origin');
        const report = info => parent.postMessage(JSON.stringify({event:'onStateChange',info}), parentOrigin);
        addEventListener('message', event => {
          if (event.source !== parent || event.origin !== parentOrigin) return;
          const message = JSON.parse(event.data);
          if (message.event === 'listening') parent.postMessage(JSON.stringify({event:'onReady'}), parentOrigin);
          if (message.event === 'command') window.commands.push(message.func);
        });
        document.getElementById('play').onclick = () => report(1);
        </script>
        """;

    private Task StubPlayersAsync() => Page.RouteAsync("https://www.youtube-nocookie.com/**",
        route => route.FulfillAsync(new() { ContentType = "text/html", Body = Stub }));

    [Test]
    public async Task Long_thread_loads_only_nearby_players_and_scroll_stress_keeps_three_unique_frames()
    {
        var requests = new ConcurrentQueue<string>();
        Page.Request += (_, request) =>
        {
            if (request.Url.StartsWith("https://www.youtube-nocookie.com/", StringComparison.Ordinal)) requests.Enqueue(request.Url);
        };
        await StubPlayersAsync();
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoAsync(LongThread);
        await Expect(Page.Locator(Cards)).ToHaveCountAsync(30);
        await Page.Locator(Cards).First.ScrollIntoViewIfNeededAsync();
        await Expect(Page.Locator(Cards).First.Locator("iframe")).ToHaveCountAsync(1);
        Assert.That(requests, Is.Not.Empty);
        Assert.That(requests.Any(url => url.Contains("QZ000000030", StringComparison.Ordinal)), Is.False);
        Assert.That(await Page.Locator(Cards).EvaluateAllAsync<bool>("cards => cards.filter(c => c.querySelector('iframe')).every(c => { const r=c.querySelector('[data-video-viewport]').getBoundingClientRect(); return r.bottom >= -300 && r.top <= innerHeight+300; })"), Is.True);
        await Page.EvaluateAsync("""
            () => {
              window.poolViolations = [];
              window.poolObserver = new MutationObserver(() => {
                const cards = [...document.querySelectorAll('[data-qz-forum-video]')];
                if (document.querySelectorAll('[data-qz-forum-video] iframe').length > 3 || cards.some(c => c.querySelectorAll('iframe').length > 1)) window.poolViolations.push('pool');
              });
              window.poolObserver.observe(document.body, {subtree:true, childList:true});
            }
            """);
        foreach (var index in new[] { 4, 10, 20, 29, 20, 10, 4, 0 })
        {
            var card = Page.Locator(Cards).Nth(index);
            await card.ScrollIntoViewIfNeededAsync();
            await Expect(card.Locator("iframe")).ToHaveCountAsync(1);
            var scroll = await Page.EvaluateAsync<double>("scrollY");
            await Page.WaitForTimeoutAsync(300);
            Assert.That(await Page.EvaluateAsync<double>("scrollY"), Is.EqualTo(scroll).Within(1));
        }
        Assert.That(await Page.EvaluateAsync<string[]>("window.poolViolations"), Is.Empty);
    }

    [Test]
    public async Task Bridge_validates_source_and_origin_pauses_others_and_never_plays_or_resumes()
    {
        await Page.SetViewportSizeAsync(1280, 1800);
        await StubPlayersAsync();
        await Page.GotoAsync(Archive);
        var first = Page.Locator(Cards).First;
        var second = Page.Locator(Cards).Nth(1);
        await first.ScrollIntoViewIfNeededAsync();
        await Expect(second.Locator("iframe")).ToHaveCountAsync(1);
        await Expect(Page.FrameLocator(Cards + " >> nth=1 >> iframe").GetByRole(AriaRole.Button, new() { Name = "Play fixture" })).ToBeVisibleAsync();
        await Page.FrameLocator(Cards + " >> nth=0 >> iframe").GetByRole(AriaRole.Button, new() { Name = "Play fixture" }).ClickAsync();
        var frameA = Page.Frames.Single(frame => frame.Url.Contains("/embed/M7lc1UVf-VE", StringComparison.Ordinal));
        var frameB = Page.Frames.Single(frame => frame.Url.Contains("/embed/abcdefghijk", StringComparison.Ordinal));
        await frameA.EvaluateAsync("window.commands = []");
        await Page.EvaluateAsync("""
            () => {
              const source = document.querySelectorAll('[data-qz-forum-video] iframe')[1].contentWindow;
              const data = JSON.stringify({event:'onStateChange',info:1});
              dispatchEvent(new MessageEvent('message', {origin:'https://evil.example', source, data}));
              dispatchEvent(new MessageEvent('message', {origin:'https://www.youtube-nocookie.com', source:window, data}));
            }
            """);
        Assert.That(await frameA.EvaluateAsync<string[]>("window.commands"), Is.Empty);
        await frameB.GetByRole(AriaRole.Button, new() { Name = "Play fixture" }).ClickAsync();
        await frameA.WaitForFunctionAsync("window.commands.includes('pauseVideo')");
        await frameA.EvaluateAsync("window.commands = []");
        await frameB.EvaluateAsync("window.commands = []");
        await Page.EvaluateAsync("Object.defineProperty(document, 'hidden', {configurable:true, value:true}); document.dispatchEvent(new Event('visibilitychange'))");
        await frameA.WaitForFunctionAsync("window.commands.includes('pauseVideo')");
        await frameB.WaitForFunctionAsync("window.commands.includes('pauseVideo')");
        await Page.EvaluateAsync("Object.defineProperty(document, 'hidden', {configurable:true, value:false}); document.dispatchEvent(new Event('visibilitychange'))");
        Assert.That(await frameA.EvaluateAsync<string[]>("window.commands"), Is.All.EqualTo("pauseVideo"));
        Assert.That(await frameB.EvaluateAsync<string[]>("window.commands"), Is.All.EqualTo("pauseVideo"));
        await Expect(first.Locator("iframe")).ToHaveAttributeAsync("sandbox", "allow-scripts allow-same-origin");
        await Expect(first.Locator("iframe")).ToHaveAttributeAsync("allow", "encrypted-media; fullscreen");
        await Expect(first.Locator("iframe")).ToHaveAttributeAsync("referrerpolicy", "strict-origin-when-cross-origin");
        var sourceUrl = await first.Locator("iframe").GetAttributeAsync("src");
        Assert.That(sourceUrl, Does.Contain("autoplay=0&playsinline=1&enablejsapi=1&origin="));
        Assert.That(sourceUrl, Does.EndWith("&start=90"));
    }

    [TestCase(320)]
    [TestCase(375)]
    [TestCase(1280)]
    public async Task Player_viewport_reserves_space_and_meets_minimum_without_overflow(int width)
    {
        await Page.SetViewportSizeAsync(width, 800);
        await Page.AddInitScriptAsync("Object.defineProperty(navigator, 'onLine', {configurable:true, value:false})");
        await StubPlayersAsync();
        await Page.GotoAsync(Archive);
        var card = Page.Locator(Cards).First;
        await card.ScrollIntoViewIfNeededAsync();
        var before = await card.Locator("[data-video-viewport]").BoundingBoxAsync();
        var link = card.GetByRole(AriaRole.Link, new() { Name = "Watch on YouTube", Exact = true });
        await link.FocusAsync();
        var scroll = await Page.EvaluateAsync<double>("scrollY");
        await Page.EvaluateAsync("Object.defineProperty(navigator, 'onLine', {configurable:true, value:true}); dispatchEvent(new Event('resize'))");
        await Expect(card.Locator("iframe")).ToHaveCountAsync(1);
        await Expect(link).ToBeFocusedAsync();
        var after = await card.Locator("iframe").BoundingBoxAsync();
        Assert.That(after, Is.Not.Null);
        Assert.That(after!.Width, Is.GreaterThanOrEqualTo(200));
        Assert.That(after.Height, Is.GreaterThanOrEqualTo(200));
        Assert.That(after.Width, Is.EqualTo(before!.Width).Within(1));
        Assert.That(after.Height, Is.EqualTo(before.Height).Within(1));
        Assert.That(await Page.EvaluateAsync<double>("scrollY"), Is.EqualTo(scroll).Within(1));
        Assert.That(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"), Is.True);
    }

    [Test]
    public async Task Eviction_keeps_the_reserved_box_and_scroll_position()
    {
        await StubPlayersAsync();
        await Page.GotoAsync(LongThread);
        var first = Page.Locator(Cards).First;
        await first.ScrollIntoViewIfNeededAsync();
        await Expect(first.Locator("iframe")).ToHaveCountAsync(1);
        var before = await first.Locator("[data-video-viewport]").BoundingBoxAsync();
        await Page.Locator(Cards).Nth(10).ScrollIntoViewIfNeededAsync();
        var scroll = await Page.EvaluateAsync<double>("scrollY");
        await Expect(first.Locator("iframe")).ToHaveCountAsync(0);
        var after = await first.Locator("[data-video-viewport]").BoundingBoxAsync();
        Assert.That(after!.Height, Is.EqualTo(before!.Height).Within(1));
        Assert.That(after.Width, Is.EqualTo(before.Width).Within(1));
        Assert.That(await Page.EvaluateAsync<double>("scrollY"), Is.EqualTo(scroll).Within(1));
    }

    [Test]
    public async Task Javascript_disabled_and_offline_players_retain_readable_links_and_prose()
    {
        var noScript = await CreateExtraContextAsync(new() { BaseURL = BaseUrl, JavaScriptEnabled = false });
        var page = await noScript.NewPageAsync();
        await page.GotoAsync(Archive);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Load YouTube video" }).First).ToBeHiddenAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Watch on YouTube", Exact = true })).ToHaveCountAsync(2);
        await Expect(page.Locator("iframe")).ToHaveCountAsync(0);
        await Page.AddInitScriptAsync("Object.defineProperty(navigator, 'onLine', {value:false})");
        await Page.GotoAsync(Archive);
        await Page.Locator(Cards).First.ScrollIntoViewIfNeededAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Load YouTube video", Exact = true }).First.ClickAsync();
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
        await Expect(Page.GetByText("Before the shared video.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("After the shared video.", new() { Exact = true })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Blocked_player_shows_retry_without_automatic_retry()
    {
        var requests = 0;
        await Page.RouteAsync("https://www.youtube-nocookie.com/**", route =>
        {
            Interlocked.Increment(ref requests);
            return route.AbortAsync("blockedbyclient");
        });
        await Page.GotoAsync(Archive);
        await Page.Locator(Cards).First.ScrollIntoViewIfNeededAsync();
        await Expect(Page.Locator(Cards).First.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0, new() { Timeout = 15000 });
        var failedRequests = requests;
        await Page.EvaluateAsync("dispatchEvent(new Event('resize'))");
        await Page.WaitForTimeoutAsync(400);
        Assert.That(requests, Is.EqualTo(failedRequests));
        await Expect(Page.Locator("[data-video-status]").First).ToContainTextAsync("Watch on YouTube");
        await Expect(Page.Locator("[data-video-status]").First).Not.ToContainTextAsync("Playing");
        await Page.Locator(Cards).First.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await Expect(Page.Locator(Cards).First.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
        Assert.That(requests, Is.GreaterThan(failedRequests));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Missing_observer_or_mismatched_origin_falls_back_to_click_to_load(bool missingObserver)
    {
        if (missingObserver)
        {
            await Page.AddInitScriptAsync("window.IntersectionObserver = undefined");
        }
        else
        {
            await Page.RouteAsync("**/forum/topic/1030/**", async route =>
            {
                var response = await route.FetchAsync();
                var body = await response.TextAsync();
                var origin = new Uri(BaseUrl).GetLeftPart(UriPartial.Authority);
                await route.FulfillAsync(new() { Response = response, Body = body.Replace($"data-qz-player-origin=\"{origin}\"", "data-qz-player-origin=\"https://other.example\"", StringComparison.Ordinal) });
            });
        }
        await StubPlayersAsync();
        await Page.GotoAsync(Archive);
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Load YouTube video", Exact = true }).First.ClickAsync();
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(1);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Unload YouTube video", Exact = true }).ClickAsync();
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Removed_post_releases_pool_and_stale_callbacks_cannot_remount()
    {
        await StubPlayersAsync();
        await Page.GotoAsync(Archive);
        await Page.Locator(Cards).First.ScrollIntoViewIfNeededAsync();
        await Expect(Page.Locator(Cards).First.Locator("iframe")).ToHaveCountAsync(1);
        await Page.EvaluateAsync("document.querySelector('.qz-forum-post').remove()");
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
        await Page.EvaluateAsync("dispatchEvent(new Event('resize')); dispatchEvent(new Event('scroll'))");
        await Page.WaitForTimeoutAsync(300);
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
        await Page.GotoAsync(Archive + "/page/2");
        await Expect(Page.Locator(Cards)).ToHaveCountAsync(2);
        await Expect(Page.GetByText("Duplicate link", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Quoted video", new() { Exact = true })).ToBeVisibleAsync();
    }
    [Test]
    public async Task Modern_post_and_quote_keep_original_content_without_player_markup()
    {
        await Context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            ["X-Test-Member-Id"] = Guid.NewGuid().ToString(),
            ["X-Test-Member-Name"] = "Video Browser Fan",
        });
        await Page.GotoAsync("/forum/c/the-music/new-thread");
        await Page.GetByLabel("Subject").FillAsync("Video browser " + Guid.NewGuid().ToString("N"));
        var editor = Page.Locator("[data-testid='rich-text-editor']").Last;
        await editor.FillAsync("https://youtu.be/M7lc1UVf-VE");
        await editor.PressAsync("ControlOrMeta+A");
        await Page.Locator("button.ql-link").ClickAsync();
        await Page.Locator(".ql-tooltip input").FillAsync("https://youtu.be/M7lc1UVf-VE");
        await Page.Locator(".ql-tooltip input").PressAsync("Enter");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create thread", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-qz-forum-video]")).ToHaveCountAsync(1);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Quote", Exact = true }).ClickAsync();
        var reply = Page.Locator("[data-testid='rich-text-editor']").Last;
        await Expect(reply).ToContainTextAsync("https://youtu.be/M7lc1UVf-VE");
        await Expect(reply).Not.ToContainTextAsync("Load YouTube video");
        await Expect(reply.Locator("iframe,[data-qz-forum-video]")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reply", Exact = true }).ClickAsync();
        await Expect(Page.Locator(".qz-forum-post")).ToHaveCountAsync(2);
        await Expect(Page.Locator("[data-qz-forum-video]")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Back_navigation_reinitialises_loader_without_playback()
    {
        await StubPlayersAsync();
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoAsync(LongThread);
        await Page.Locator(Cards).First.ScrollIntoViewIfNeededAsync();
        await Expect(Page.Locator(Cards).First.Locator("iframe")).ToHaveCountAsync(1);

        await Page.GotoAsync("/");
        await Page.GoBackAsync();
        await Expect(Page.Locator(Cards)).ToHaveCountAsync(30);
        await Page.EvaluateAsync("""
            () => {
              dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
              dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
            }
            """);

        await Page.Locator(Cards).First.ScrollIntoViewIfNeededAsync();
        await Expect(Page.Locator(Cards).First.Locator("iframe")).ToHaveCountAsync(1);

        await Page.AddInitScriptAsync("window.IntersectionObserver = undefined");
        await Page.GotoAsync(Archive);
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
        await Page.EvaluateAsync("""
            () => {
              dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
              dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
            }
            """);
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Load YouTube video", Exact = true }).First.ClickAsync();
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(1);
        Assert.That(await Page.EvaluateAsync<bool>("""
            () => [...document.querySelectorAll('iframe')].every(frame => {
              try { return !(frame.contentWindow.commands || []).includes('playVideo'); }
              catch { return true; }
            })
            """), Is.True);
    }
}
