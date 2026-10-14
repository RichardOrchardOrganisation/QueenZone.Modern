using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public class AdminMembersProofTests : AdminFeatureProofTest
{
    [Test]
    public Task Index_ProvesAnonymousMemberAndAdminAccess() =>
        AssertAdminFeatureAsync("web.admin.members.index", "/admin/members");

    [TestCase("Suspend")]
    [TestCase("Reinstate")]
    [TestCase("HideForumContent")]
    [TestCase("UnhideForumContent")]
    public Task MemberHandlers_RejectUnauthorisedRequests(string handler) =>
        AssertPostAccessAsync($"/admin/members/{Guid.NewGuid()}/{handler}");

    [TestCase("Hide")]
    [TestCase("Unhide")]
    public Task LegacyAuthorHandlers_RejectUnauthorisedRequests(string handler) =>
        AssertPostAccessAsync($"/admin/members/forum-author/{handler}");

    [Test]
    public async Task MemberReview_ProvesDetailAndCancelsHideWithoutMutation()
    {
        var name = FixturePrefix + Guid.NewGuid().ToString("N");
        await Page.GotoAsync("/admin/reviewer-accounts");
        await Page.Locator("#Input_Email").FillAsync(name + "@example.com");
        await Page.Locator("#Input_DisplayName").FillAsync(name);
        var password = Guid.NewGuid().ToString("N") + "aA1!";
        await Page.Locator("#Input_Password").FillAsync(password);
        await Page.Locator("#Input_ConfirmPassword").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();
        await AssertAdminFeatureAsync("web.admin.members.index", "/admin/members?query=" + name);
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row).ToContainTextAsync("Active");
        var review = row.GetByRole(AriaRole.Link, new() { Name = "Review" });
        var path = (await review.GetAttributeAsync("href"))!;
        var id = Guid.Parse(path.Split('/')[^1]);
        var topic = await CreateTopicAsync(id, name);
        await review.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true })).ToBeVisibleAsync();
        await AssertAdminFeatureAsync("web.admin.members.detail", path);
        await Expect(Page.Locator(".admin-detail-list")).ToContainTextAsync(name + "@example.com");
        await Expect(Page.Locator(".admin-detail-list")).ToContainTextAsync("Active");
        await Expect(Page.GetByText("1 posts and 1 started threads match this author.", new() { Exact = true })).ToBeVisibleAsync();
        var suspend = Page.Locator($"form[action='{path}/Suspend']");
        await Expect(suspend.Locator("textarea[name=Reason]")).ToHaveAttributeAsync("required", "");
        await Expect(suspend.GetByRole(AriaRole.Button, new() { Name = "Suspend member" })).ToBeVisibleAsync();
        await AssertAdminFeatureAsync("web.admin.members.action", path);
        await ProveCancelAsync($"form[action='{path}/HideForumContent']", "member-hide-confirmation");
        await Expect(Page.Locator(".admin-detail-list")).ToContainTextAsync("Active");
        await Expect(Page.GetByText("1 posts and 1 started threads match this author.", new() { Exact = true })).ToBeVisibleAsync();
        await topic.ReloadAsync();
        await Expect(topic.Locator(".qz-forum-post").Filter(new() { HasText = name })).ToBeVisibleAsync();
    }

    [Test]
    public async Task LegacyAuthor_ProvesConfirmationAndCancelsWithoutMutation()
    {
        var name = FixturePrefix + Guid.NewGuid().ToString("N");
        var topic = await CreateTopicAsync(Guid.NewGuid(), name);
        await AssertAdminFeatureAsync("web.admin.members.forumAuthorAction", "/admin/members?query=" + name);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("No member account.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.Locator("input[name=DisplayName]")).ToHaveValueAsync(name);
        await ProveCancelAsync("form[action='/admin/members/forum-author/Hide']", "legacy-hide-confirmation");
        await Expect(Page.GetByText("1 posts and 1 started threads", new() { Exact = false })).ToBeVisibleAsync();
        await topic.ReloadAsync();
        await Expect(topic.Locator(".qz-forum-post").Filter(new() { HasText = name })).ToBeVisibleAsync();
    }

    private async Task<IPage> CreateTopicAsync(Guid memberId, string name)
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

    private async Task ProveCancelAsync(string formSelector, string screenshotName)
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

    private async Task AssertPostAccessAsync(string path)
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
