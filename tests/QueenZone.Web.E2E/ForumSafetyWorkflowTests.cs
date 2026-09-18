using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class ForumSafetyWorkflowTests : E2EPageTest
{
    private const string MemberIdHeader = "X-Test-Member-Id";
    private const string MemberNameHeader = "X-Test-Member-Name";
    private const string AdminEmailHeader = "X-Test-User-Email";

    [Test]
    public async Task MemberReportsAnotherPostAndAdminReviewsIt()
    {
        var authorId = Guid.NewGuid();
        var reporterId = Guid.NewGuid();
        var authorName = $"E2E forum author {authorId:N}";
        var subject = $"E2E safety {authorId:N}";
        var body = $"E2E reported body {authorId:N}";

        var authorContext = await CreateMemberContextAsync(authorId, authorName);
        var authorPage = await authorContext.NewPageAsync();
        await authorPage.GotoAsync("/forum/c/the-music/new-thread");
        await authorPage.GetByLabel("Subject").FillAsync(subject);
        await FillEditorAsync(authorPage, body);
        await authorPage.GetByRole(AriaRole.Button, new() { Name = "Create thread" }).ClickAsync();
        await Expect(authorPage).ToHaveURLAsync(new Regex(".*/forum/topic/\\d+/e2e-safety-.*"));
        var topicUrl = authorPage.Url;
        var post = authorPage.Locator(".qz-forum-post").Filter(new() { HasText = body });
        var postId = (await post.GetAttributeAsync("id"))?["post-".Length..];
        Assert.That(postId, Is.Not.Null.And.Not.Empty);

        var ownReport = await authorPage.GotoAsync($"/forum/post/{postId}/report");
        Assert.That(ownReport?.Status, Is.EqualTo(400), "Authors must not report their own posts.");

        var reporterContext = await CreateMemberContextAsync(reporterId, "E2E safety reporter");
        var reporterPage = await reporterContext.NewPageAsync();
        await reporterPage.GotoAsync(topicUrl);
        var reportedPost = reporterPage.Locator($"#post-{postId}");
        await reportedPost.GetByRole(AriaRole.Link, new() { Name = "Report post" }).ClickAsync();
        await Expect(reporterPage.GetByRole(AriaRole.Heading, new() { Name = "Report forum post" })).ToBeVisibleAsync();
        await reporterPage.GetByLabel("Reason").SelectOptionAsync("Harassment or bullying");
        await reporterPage.GetByLabel("Supporting details (optional)").FillAsync("E2E safety evidence");
        await reporterPage.GetByRole(AriaRole.Button, new() { Name = "Submit report" }).ClickAsync();
        await Expect(reporterPage).ToHaveURLAsync(new Regex(".*/forum/topic/\\d+/e2e-safety-.*#post-\\d+$"));
        await Expect(reporterPage.Locator(".qz-account-notice[role='status']")).ToContainTextAsync("Report submitted");
        await Expect(reportedPost.GetByRole(AriaRole.Link, new() { Name = "Report post" })).ToHaveCountAsync(0);

        // A second form submission remains idempotent even though the topic no longer offers the link.
        await reporterPage.GotoAsync($"/forum/post/{postId}/report");
        await reporterPage.GetByLabel("Reason").SelectOptionAsync("Other");
        await reporterPage.GetByRole(AriaRole.Button, new() { Name = "Submit report" }).ClickAsync();
        await Expect(reporterPage.Locator(".qz-account-notice[role='status']")).ToContainTextAsync("Report already submitted");

        var adminContext = await CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                [AdminEmailHeader] = Environment.GetEnvironmentVariable("E2E_ADMIN_EMAIL") ?? "admin@test.local",
            },
        });
        var adminPage = await adminContext.NewPageAsync();
        await adminPage.GotoAsync("/admin/forum-reports");
        var reportRow = adminPage.GetByRole(AriaRole.Row).Filter(new() { HasText = authorName });
        await Expect(reportRow).ToBeVisibleAsync();
        await reportRow.GetByRole(AriaRole.Link, new() { Name = "Open" }).ClickAsync();
        await Expect(adminPage.GetByRole(AriaRole.Blockquote)).ToContainTextAsync(body);
        await adminPage.GetByLabel("Status").SelectOptionAsync("Reviewed");
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await Expect(adminPage.GetByRole(AriaRole.Status)).ToContainTextAsync("Marked as Reviewed");
        await adminPage.ReloadAsync();
        await Expect(adminPage.GetByLabel("Status")).ToHaveValueAsync("Reviewed");
    }

    private async Task<IBrowserContext> CreateMemberContextAsync(Guid id, string name) =>
        await CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                [MemberIdHeader] = id.ToString(),
                [MemberNameHeader] = name,
            },
        });

    private static async Task FillEditorAsync(IPage page, string body)
    {
        var editor = page.Locator("[data-testid='rich-text-editor']");
        await editor.ClickAsync();
        await page.Keyboard.InsertTextAsync(body);
    }
}
