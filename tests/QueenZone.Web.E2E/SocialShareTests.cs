using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class SocialShareTests : E2EPageTest
{
    [Test]
    public async Task NewsDetail_ShowsStaticShareLinksOnDesktop()
    {
        await Page.GotoAsync("/news/1003/queenzone-modernisation-begins");

        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Share on X" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Share on Facebook" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Share on WhatsApp" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Share by email" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Share", Exact = true })).ToBeHiddenAsync();
    }

    [Test]
    public async Task NewsDetail_UsesOsShareSheetOnTouchWhenAvailable()
    {
        var context = await CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            HasTouch = true,
            IsMobile = true,
            ViewportSize = new() { Width = 390, Height = 844 },
        });
        await context.AddInitScriptAsync("""
            const originalMatchMedia = window.matchMedia.bind(window);
            window.matchMedia = (query) => {
              if (String(query).includes("pointer: coarse")) {
                return {
                  matches: true,
                  media: query,
                  onchange: null,
                  addListener() {},
                  removeListener() {},
                  addEventListener() {},
                  removeEventListener() {},
                  dispatchEvent() { return false; }
                };
              }
              return originalMatchMedia(query);
            };
            Object.defineProperty(navigator, "share", {
              configurable: true,
              value: async (data) => { window.__shared = data; }
            });
            Object.defineProperty(navigator, "canShare", {
              configurable: true,
              value: () => true
            });
            """);

        var page = await context.NewPageAsync();
        await page.GotoAsync("/news/1003/queenzone-modernisation-begins");

        var nativeShare = page.GetByRole(AriaRole.Button, new() { Name = "Share", Exact = true });
        await Expect(nativeShare).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Share on X" })).ToBeHiddenAsync();

        await nativeShare.ClickAsync();

        var title = await page.EvaluateAsync<string>("() => window.__shared && window.__shared.title");
        var url = await page.EvaluateAsync<string>("() => window.__shared && window.__shared.url");
        Assert.That(title, Does.Contain("QueenZone modernisation begins"));
        Assert.That(url, Does.Contain("/news/1003/queenzone-modernisation-begins"));
    }
}
