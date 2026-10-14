using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

public abstract class AdminMemberContentProofTest : AdminFeatureProofTest
{
    protected async Task<IPage> CreateTopicAsync(Guid memberId, string name)
    {
        var context = await CreateExtraContextAsync(new()
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["X-Test-Member-Id"] = memberId.ToString(),
                ["X-Test-Member-Name"] = name
            }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/forum/c/the-music/new-thread");
        await page.GetByLabel("Subject").FillAsync(name);
        await page.Locator("[data-testid='rich-text-editor']").FillAsync(name);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create thread" }).ClickAsync();
        await Expect(page.Locator(".qz-forum-post").Filter(new() { HasText = name })).ToBeVisibleAsync();
        return page;
    }

    protected async Task ProveCancelAsync(string formSelector, string screenshotName)
    {
        var posts = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.Method == "POST" && new Uri(request.Url).AbsolutePath.StartsWith("/admin/members", StringComparison.Ordinal))
            {
                posts.Add(request.Url);
            }
        };
        var form = Page.Locator(formSelector);
        await form.Locator("[data-confirm-dialog-open]").ClickAsync();
        var dialog = form.Locator("dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.Locator("button[type=submit]")).ToBeVisibleAsync();
        var artifacts = E2EArtifactPaths.EnsureDirectory();
        await Page.ScreenshotAsync(new() { Path = Path.Combine(artifacts, screenshotName + ".png"), FullPage = true });
        await dialog.Locator("[data-confirm-dialog-cancel]").ClickAsync();
        await Expect(dialog).Not.ToBeVisibleAsync();
        await Page.ReloadAsync();
        Assert.That(posts, Is.Empty, "Confirmation-only proof must not submit an admin mutation.");
        await Expect(Page.Locator(formSelector)).ToBeVisibleAsync();
        // Leave the real confirmation visible for the shared passing-state proof capture.
        await Page.Locator(formSelector).Locator("[data-confirm-dialog-open]").ClickAsync();
        await Expect(Page.Locator(formSelector).Locator("dialog")).ToBeVisibleAsync();
    }

    protected async Task AssertPostAccessAsync(string path)
    {
        var target = new Uri(new Uri(BaseUrl), path);
        await using var anonymous = await Playwright.APIRequest.NewContextAsync();
        var challenge = await anonymous.PostAsync(target.ToString(), new() { MaxRedirects = 0 });
        Assert.That(challenge.Status, Is.AnyOf(401, 302), "Anonymous action requests must be challenged before handler execution.");
        if (challenge.Status == 302)
        {
            Assert.That(challenge.Headers.TryGetValue("location", out var location), Is.True);
            Assert.That(new Uri(target, location!).AbsolutePath, Does.Match("(?i)(signin|sign-in|login|log-in)"));
        }
        await using var member = await Playwright.APIRequest.NewContextAsync(new()
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User-Email"] = "member@test.local" }
        });
        var forbidden = await member.PostAsync(target.ToString(), new() { MaxRedirects = 0 });
        Assert.That(forbidden.Status, Is.EqualTo(403), "Non-admin requests must be forbidden before antiforgery or the handler.");
    }
}
