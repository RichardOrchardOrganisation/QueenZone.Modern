using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public class AdminTriageProofTests : AdminFeatureProofTest
{
    [Test]
    public async Task ForumReports_FixtureStatusChange()
    {
        var marker = FixturePrefix + Guid.NewGuid().ToString("N");
        var author = await MemberPageAsync(Guid.NewGuid(), marker);
        await author.GotoAsync("/forum/c/the-music/new-thread");
        await author.GetByLabel("Subject").FillAsync(marker);
        await author.Locator("[data-testid='rich-text-editor']").ClickAsync();
        await author.Keyboard.InsertTextAsync(marker + " reported post body");
        await author.GetByRole(AriaRole.Button, new() { Name = "Create thread" }).ClickAsync();
        await Expect(author).ToHaveURLAsync(new Regex("/forum/topic/\\d+/"));
        var post = author.Locator(".qz-forum-post").Filter(new() { HasText = marker + " reported post body" });
        var postAnchor = await post.GetAttributeAsync("id");
        Assert.That(postAnchor, Does.StartWith("post-"));
        var reporter = await MemberPageAsync(Guid.NewGuid(), marker + "-reporter");
        await reporter.GotoAsync($"/forum/post/{postAnchor![5..]}/report");
        await reporter.GetByLabel("Reason").SelectOptionAsync("Other");
        await reporter.GetByLabel("Supporting details (optional)").FillAsync(marker);
        await reporter.GetByRole(AriaRole.Button, new() { Name = "Submit report" }).ClickAsync();
        await Expect(reporter.GetByRole(AriaRole.Status).Filter(new() { HasText = "Report submitted." })).ToBeVisibleAsync();

        await AssertAdminFeatureAsync("web.admin.forumReports.index", "/admin/forum-reports");
        var path = await OpenFixtureAsync(marker);
        await AssertAdminFeatureAsync("web.admin.forumReports.detail", path);
        await Expect(Page.GetByRole(AriaRole.Blockquote)).ToContainTextAsync(marker);
        await AssertAdminHandlerAccessAsync("web.admin.forumReports.action", path + "/status");
        await SaveStatusAsync("Reviewed");
    }

    [Test]
    public async Task HelpRequests_FixtureStatusAndNotesChange()
    {
        var marker = FixturePrefix + Guid.NewGuid().ToString("N");
        var context = await CreateExtraContextAsync(new() { BaseURL = BaseUrl });
        var guest = await context.NewPageAsync();
        await guest.GotoAsync("/contact");
        await guest.GetByLabel("Subject").FillAsync(marker);
        await guest.GetByLabel("Your name").FillAsync(marker);
        await guest.GetByLabel("Email address").FillAsync(marker + "@example.com");
        await guest.GetByLabel("Your message").FillAsync(marker + " private help request");
        await guest.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();
        await Expect(guest).ToHaveURLAsync(new Regex("/contact/confirmation$"));

        await AssertAdminFeatureAsync("web.admin.helpRequests.index", "/admin/help");
        var path = await OpenFixtureAsync(marker);
        await AssertAdminFeatureAsync("web.admin.helpRequests.detail", path);
        await AssertAdminHandlerAccessAsync("web.admin.helpRequests.action", path + "/status");
        await Page.GetByLabel("Internal notes").FillAsync(marker + " reviewed");
        await SaveStatusAsync("Resolved");
        await Expect(Page.GetByLabel("Internal notes")).ToHaveValueAsync(marker + " reviewed");
    }

    [Test]
    public async Task PrivateMessages_FixtureStatusChange()
    {
        var sender = await CreateAccountAsync();
        var recipient = await CreateAccountAsync();
        var author = await MemberPageAsync(sender.Id, sender.Name);
        var reader = await MemberPageAsync(recipient.Id, recipient.Name);
        var body = sender.Name + " private report evidence";
        await author.GotoAsync($"/messages/compose?to={recipient.Id}");
        await author.GetByRole(AriaRole.Textbox, new() { Name = "Message", Exact = true }).FillAsync(body);
        await author.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();
        await Expect(author).ToHaveURLAsync(new Regex("/messages/[0-9a-f-]{36}$"));
        await reader.GotoAsync(author.Url);
        var message = reader.Locator(".qz-message-thread__item").Filter(new() { HasText = body });
        await message.GetByRole(AriaRole.Button, new() { Name = "Report message" }).ClickAsync();
        await reader.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Report message" }).ClickAsync();
        await Expect(message.GetByText("Reported", new() { Exact = true })).ToBeVisibleAsync();

        await AssertAdminFeatureAsync("web.admin.privateMessages.index", "/admin/private-messages");
        var path = await OpenFixtureAsync(sender.Name);
        await AssertAdminFeatureAsync("web.admin.privateMessages.detail", path);
        await Expect(Page.GetByRole(AriaRole.Blockquote)).ToHaveTextAsync(body);
        await AssertAdminHandlerAccessAsync("web.admin.privateMessages.action", path + "/status");
        await SaveStatusAsync("Reviewed");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FanPerformanceReports_FixtureDismissOrHide(bool hide)
    {
        var member = await CreateAccountAsync();
        var performer = await MemberPageAsync(member.Id, member.Name);
        await performer.GotoAsync("/submit/fan-performance");
        await performer.GetByLabel("Title", new() { Exact = true }).FillAsync(member.Name);
        await performer.GetByLabel("Queen song covered").FillAsync("Somebody to Love");
        await performer.GetByLabel("Performed by").FillAsync(member.Name);
        var audio = new byte[400];
        audio[0] = 0xFF;
        audio[1] = 0xFB;
        audio[2] = 0x90;
        await performer.Locator("#AudioFile").SetInputFilesAsync(new FilePayload
        {
            Name = member.Name + ".mp3",
            MimeType = "audio/mpeg",
            Buffer = audio
        });
        await performer.GetByRole(AriaRole.Checkbox, new() { NameRegex = new Regex("own performance") }).CheckAsync();
        await performer.GetByRole(AriaRole.Button, new() { Name = "Submit for review" }).ClickAsync();
        await Expect(performer).ToHaveURLAsync(new Regex("/submit/fan-performance/confirmation/"));
        await Page.GotoAsync("/admin/fan-performance-submissions");
        await Page.GetByRole(AriaRole.Row).Filter(new() { HasText = member.Name })
            .GetByRole(AriaRole.Link, new() { Name = "Review" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Approve", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("approved and published");
        var stage = (await Page.Locator("dt").Filter(new() { HasText = "Published performance" })
            .Locator("+ dd").InnerTextAsync()).Trim().TrimStart('#');
        Assert.That(stage, Does.Match("^\\d+$"));
        await performer.GotoAsync("/fan-performances");
        await Expect(performer.Locator($"li[data-qz-stage-id='{stage}']")).ToContainTextAsync(member.Name);
        await performer.GotoAsync($"/fan-performances/{stage}/report");
        await performer.GetByLabel("Reason").FillAsync(member.Name);
        await performer.GetByRole(AriaRole.Button, new() { Name = "Send report" }).ClickAsync();
        await Expect(performer.GetByRole(AriaRole.Status)).ToContainTextAsync("admin team will review");

        await AssertAdminFeatureAsync("web.admin.fanPerformanceReports.index", "/admin/fan-performance-reports");
        var path = await OpenFixtureAsync(member.Name);
        await AssertAdminFeatureAsync("web.admin.fanPerformanceReports.detail", path);
        await AssertAdminHandlerAccessAsync("web.admin.fanPerformanceReports.action", path + (hide ? "/hide" : "/dismiss"));
        if (hide)
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Hide and resolve" }).ClickAsync();
            var dialog = Page.Locator("dialog[open]");
            await Expect(dialog).ToBeVisibleAsync();
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
            await Expect(dialog).ToHaveCountAsync(0);
            await Page.ReloadAsync();
            await Expect(Page.Locator(".admin-detail-list")).ToContainTextAsync("Open");
            await performer.GotoAsync($"/fan-performances/{stage}/report");
            await Expect(performer.GetByRole(AriaRole.Heading, new() { Name = "Report a fan performance" })).ToBeVisibleAsync();
            await Page.GetByRole(AriaRole.Button, new() { Name = "Hide and resolve" }).ClickAsync();
            await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Hide and resolve" }).ClickAsync();
        }
        else
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Dismiss without hiding" }).ClickAsync();
        }
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync(hide ? "marked the report resolved" : "Dismissed the report");
        await Page.ReloadAsync();
        await Expect(Page.Locator(".admin-detail-list")).ToContainTextAsync(hide ? "Resolved" : "Dismissed");
        var response = await performer.GotoAsync($"/fan-performances/{stage}/report");
        Assert.That(response?.Status, Is.EqualTo(hide ? 404 : 200), "Only the fixture's published performance may be hidden.");
        await performer.GotoAsync("/fan-performances");
        var published = performer.Locator($"li[data-qz-stage-id='{stage}']");
        if (hide)
        {
            await Expect(published).ToHaveCountAsync(0);
        }
        else
        {
            await Expect(published).ToContainTextAsync(member.Name);
        }
    }

    private async Task<string> OpenFixtureAsync(string marker)
    {
        Assert.That(marker, Does.StartWith(FixturePrefix));
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = marker });
        await Expect(row).ToHaveCountAsync(1);
        var link = row.GetByRole(AriaRole.Link, new() { Name = "Open" });
        var path = await link.GetAttributeAsync("href");
        Assert.That(path, Is.Not.Null.And.Not.Empty);
        await link.ClickAsync();
        return path!;
    }

    private async Task SaveStatusAsync(string status)
    {
        await Page.Locator("select[name='status']").SelectOptionAsync(status);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Status)).ToContainTextAsync("Marked as " + status);
        await Page.ReloadAsync();
        await Expect(Page.Locator("select[name='status']")).ToHaveValueAsync(status);
    }

    private async Task<(Guid Id, string Name)> CreateAccountAsync()
    {
        // The admin create form makes a real in-memory member for services that resolve accounts.
        var name = FixturePrefix + Guid.NewGuid().ToString("N");
        await Page.GotoAsync("/admin/reviewer-accounts");
        await Page.Locator("#Input_Email").FillAsync(name + "@example.com");
        await Page.Locator("#Input_DisplayName").FillAsync(name);
        var password = Guid.NewGuid().ToString("N") + "aA1!";
        await Page.Locator("#Input_Password").FillAsync(password);
        await Page.Locator("#Input_ConfirmPassword").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await Expect(row).ToHaveCountAsync(1);
        var href = await row.GetByRole(AriaRole.Link, new() { Name = "Edit" }).GetAttributeAsync("href");
        return (Guid.Parse(href!.Split('/')[^1]), name);
    }

    private async Task<IPage> MemberPageAsync(Guid id, string name)
    {
        var context = await CreateExtraContextAsync(new()
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["X-Test-Member-Id"] = id.ToString(),
                ["X-Test-Member-Name"] = name,
                ["X-Test-Member-Email"] = name + "@example.com"
            }
        });
        return await context.NewPageAsync();
    }
}
