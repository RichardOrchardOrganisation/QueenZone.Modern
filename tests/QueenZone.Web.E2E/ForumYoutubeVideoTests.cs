using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public class ForumYoutubeVideoTests : E2EPageTest
{
    private const string Archive = "/forum/topic/1030/archive-sample-thread-1030";
    private const string Player = "https://www.youtube-nocookie.com/embed/M7lc1UVf-VE?autoplay=0&playsinline=1&start=90";

    [Test]
    public async Task Archive_cards_contact_no_third_party_until_keyboard_activation_and_keep_one_player()
    {
        var requests = new ConcurrentQueue<string>();
        Page.Request += (_, request) => requests.Enqueue(request.Url);
        string? referer = null;
        await Page.RouteAsync("https://www.youtube-nocookie.com/**", async route =>
        {
            referer = await route.Request.HeaderValueAsync("referer");
            await route.FulfillAsync(new() { ContentType = "text/html", Body = "<!doctype html><title>Stub player</title><button>Play fixture</button>" });
        });
        await Page.GotoAsync("/forum/1/the-music/page/2");
        await Page.GetByRole(AriaRole.Link, new() { Name = "Archive sample thread 1030", Exact = true }).ClickAsync();
        await Expect(Page.Locator("[data-qz-forum-video]")).ToHaveCountAsync(2);
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
        Assert.That(requests.Where(url => new Uri(url).Authority != new Uri(BaseUrl).Authority), Is.Empty,
            "Reading the cards must make no third-party requests on the Testing host.");
        var first = Page.Locator("[data-qz-forum-video]").First.GetByRole(AriaRole.Button);
        await first.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(first).ToBeFocusedAsync();
        await Expect(first).ToHaveTextAsync("Unload YouTube video");
        var frame = Page.Locator("iframe");
        await Expect(frame).ToHaveCountAsync(1);
        await Expect(frame).ToHaveAttributeAsync("src", Player);
        await Expect(frame).ToHaveAttributeAsync("referrerpolicy", "strict-origin-when-cross-origin");
        await Expect(frame).ToHaveAttributeAsync("allow", "encrypted-media; fullscreen");
        await Expect(frame).ToHaveAttributeAsync("sandbox", "allow-scripts allow-same-origin");
        await Expect(Page.FrameLocator("iframe").GetByRole(AriaRole.Button, new() { Name = "Play fixture" })).ToBeVisibleAsync();
        Assert.That(referer, Is.EqualTo(new Uri(BaseUrl).GetLeftPart(UriPartial.Authority) + "/"));
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Watch on YouTube", Exact = true })).ToHaveCountAsync(2);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Load YouTube video", Exact = true }).ClickAsync();
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(1);
        await Expect(Page.Locator("iframe")).ToHaveAttributeAsync("src", "https://www.youtube-nocookie.com/embed/abcdefghijk?autoplay=0&playsinline=1");
        await Expect(first).ToHaveTextAsync("Load YouTube video");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Unload YouTube video", Exact = true }).ClickAsync();
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
        await first.ClickAsync();
        await Page.Locator("a.archive-pagination-next").First.ClickAsync();
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-qz-forum-video]")).ToHaveCountAsync(2);
        await Expect(Page.GetByText("Duplicate link", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Quoted video", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("sentence link", new() { Exact = true })).ToBeVisibleAsync();
    }

    [TestCase(320)]
    [TestCase(375)]
    [TestCase(1280)]
    public async Task Player_viewport_reserves_space_and_meets_minimum_without_overflow(int width)
    {
        await Page.SetViewportSizeAsync(width, 800);
        await Page.RouteAsync("https://www.youtube-nocookie.com/**", route => route.FulfillAsync(new() { ContentType = "text/html", Body = "<p>Player fixture</p>" }));
        await Page.GotoAsync(Archive);
        var before = await Page.Locator("[data-video-viewport]").First.BoundingBoxAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Load YouTube video", Exact = true }).First.ClickAsync();
        var after = await Page.Locator("iframe").BoundingBoxAsync();
        Assert.That(after, Is.Not.Null);
        Assert.That(after!.Width, Is.GreaterThanOrEqualTo(200));
        Assert.That(after.Height, Is.GreaterThanOrEqualTo(200));
        Assert.That(after.Width, Is.EqualTo(before!.Width).Within(1));
        Assert.That(after.Height, Is.EqualTo(before.Height).Within(1));
        Assert.That(await Page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"), Is.True);
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
        await Page.GotoAsync(Archive);
        await Context.SetOfflineAsync(true);
        try
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Load YouTube video", Exact = true }).First.ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Watch on YouTube", Exact = true }).First).ToBeVisibleAsync();
            await Expect(Page.GetByText("Before the shared video.", new() { Exact = true })).ToBeVisibleAsync();
            await Expect(Page.GetByText("After the shared video.", new() { Exact = true })).ToBeVisibleAsync();
        }
        finally
        {
            await Context.SetOfflineAsync(false);
        }
    }

    [Test]
    public async Task Blocked_player_keeps_external_fallback_and_does_not_claim_playback_success()
    {
        await Page.RouteAsync("https://www.youtube-nocookie.com/**", route => route.AbortAsync("blockedbyclient"));
        await Page.GotoAsync(Archive);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Load YouTube video", Exact = true }).First.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Watch on YouTube", Exact = true }).First).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-video-status]").First).ToContainTextAsync("Watch on YouTube");
        await Expect(Page.Locator("[data-video-status]").First).Not.ToContainTextAsync("Playing");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Unload YouTube video", Exact = true }).ClickAsync();
        await Expect(Page.Locator("iframe")).ToHaveCountAsync(0);
        await Expect(Page.Locator("[data-video-placeholder]").First).ToBeVisibleAsync();
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
}
