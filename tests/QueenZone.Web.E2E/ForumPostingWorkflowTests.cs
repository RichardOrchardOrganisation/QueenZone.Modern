using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class ForumPostingWorkflowTests : E2EPageTest
{
    private static readonly Regex NewTopicUrl = new(".*/forum/topic/\\d+/playwright-forum-topic-.*", RegexOptions.IgnoreCase);
    private const string TestMemberIdHeader = "X-Test-Member-Id";
    private const string TestMemberNameHeader = "X-Test-Member-Name";
    private const string TestMemberName = "Playwright Forum Fan";

    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                [TestMemberIdHeader] = Guid.NewGuid().ToString(),
                [TestMemberNameHeader] = TestMemberName,
            }
        };

    [Test]
    public async Task MemberCanCreateTopicAndReply()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var subject = $"Playwright forum topic {stamp}";
        var firstBody = $"Playwright first post {stamp}";
        var replyBody = $"Playwright reply {stamp}";

        await Page.GotoAsync("/forum/1/the-music");
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "New thread" })).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Link, new() { Name = "New thread" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "New thread", Level = 1 })).ToBeVisibleAsync();

        await Page.GetByLabel("Subject").FillAsync(subject);
        await FillRichTextEditorAsync(firstBody);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create thread" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(NewTopicUrl);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = subject, Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.Locator(".qz-forum-post").Filter(new() { HasText = firstBody })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = TestMemberName, Exact = true })).ToBeVisibleAsync();

        await FillRichTextEditorAsync(replyBody);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reply" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(".*/forum/topic/\\d+/playwright-forum-topic-.*#post-\\d+", RegexOptions.IgnoreCase));
        await Expect(Page.Locator(".qz-forum-post").Filter(new() { HasText = replyBody })).ToBeVisibleAsync();
    }

    [Test]
    public async Task MemberCanCreateTopicWithPoll()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var subject = $"Playwright forum poll topic {stamp}";
        var question = $"Best Queen poll option {stamp}?";

        await Page.GotoAsync("/forum/c/the-music/new-thread");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "New thread", Level = 1 })).ToBeVisibleAsync();

        await Page.GetByLabel("Subject").FillAsync(subject);
        await FillRichTextEditorAsync($"Playwright poll first post {stamp}");
        await Page.GetByLabel("Add a poll").CheckAsync();
        await Page.GetByLabel("Poll question").FillAsync(question);
        await Page.GetByPlaceholder("Option 1").FillAsync("Seventies");
        await Page.GetByPlaceholder("Option 2").FillAsync("Eighties");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add option" }).ClickAsync();
        await Page.GetByPlaceholder("Option").Last.FillAsync("Nineties");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add option" }).ClickAsync();
        await Page.GetByPlaceholder("Option").Last.FillAsync("Now");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Create thread" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(".*/forum/topic/\\d+/playwright-forum-poll-topic-.*", RegexOptions.IgnoreCase));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = subject, Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = question, Level = 2 })).ToBeVisibleAsync();
        await Expect(Page.GetByText("Seventies")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Eighties")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Nineties")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Now")).ToBeVisibleAsync();
    }

    [Test]
    public async Task MemberCanEditOwnPost()
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var subject = $"Playwright forum edit topic {stamp}";
        var originalBody = $"Playwright original post {stamp}";
        var editedBody = $"Playwright edited post {stamp}";

        await Page.GotoAsync("/forum/c/the-music/new-thread");
        await Page.GetByLabel("Subject").FillAsync(subject);
        await FillRichTextEditorAsync(originalBody);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create thread" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/forum/topic/\\d+/playwright-forum-edit-topic-.*", RegexOptions.IgnoreCase));

        var post = Page.Locator(".qz-forum-post").Filter(new() { HasText = originalBody });
        await Expect(post).ToBeVisibleAsync();
        await post.GetByRole(AriaRole.Link, new() { Name = "Edit" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Edit post", Level = 1 })).ToBeVisibleAsync();
        var editor = Page.Locator("[data-testid='rich-text-editor']").Last;
        await Expect(editor).ToContainTextAsync(originalBody);
        await editor.ClickAsync();
        await Page.Keyboard.PressAsync("ControlOrMeta+A");
        await Page.Keyboard.InsertTextAsync(editedBody);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(".*/forum/topic/\\d+/playwright-forum-edit-topic-.*#post-\\d+", RegexOptions.IgnoreCase));
        await Expect(Page.Locator(".qz-forum-post").Filter(new() { HasText = editedBody })).ToBeVisibleAsync();
        await Expect(Page.GetByText(originalBody)).ToHaveCountAsync(0);
    }

    [TestCase("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation")]
    [TestCase("ppt", "application/vnd.ms-powerpoint")]
    public async Task MemberCanUploadAndDownloadPowerPoint(string extension, string expected)
    {
        var fileName = $"deck-{Guid.NewGuid():N}.{extension}";
        byte[] bytes = extension == "pptx" ? [0x50, 0x4B, 0x03, 0x04] : [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        await Page.GotoAsync("/forum/topic/1002/ranking-every-studio-album");
        await FillRichTextEditorAsync("PowerPoint upload regression");
        await Page.GetByLabel("Attachments (optional)").SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "application/octet-stream",
            Buffer = bytes,
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reply", Exact = true }).ClickAsync();
        var link = Page.GetByRole(AriaRole.Link, new() { Name = fileName, Exact = false });
        await Expect(link).ToBeVisibleAsync();
        var path = await link.GetAttributeAsync("href");
        var response = await Context.APIRequest.GetAsync(path!);
        Assert.That(response.Status, Is.EqualTo(200));
        Assert.That(response.Headers["content-type"], Is.EqualTo(expected));
        Assert.That(await response.BodyAsync(), Is.EqualTo(bytes));
    }

    [TestCase("pptx")]
    [TestCase("ppt")]
    public async Task MemberCannotUploadHtmlAsPowerPoint(string extension)
    {
        var fileName = $"fake-{Guid.NewGuid():N}.{extension}";
        await Page.GotoAsync("/forum/topic/1002/ranking-every-studio-album");
        await FillRichTextEditorAsync("Spoofed PowerPoint regression");
        await Page.GetByLabel("Attachments (optional)").SetInputFilesAsync(new FilePayload
        {
            Name = fileName,
            MimeType = "application/vnd.ms-powerpoint",
            Buffer = "<html>not a presentation</html>"u8.ToArray(),
        });
        await Page.GetByRole(AriaRole.Button, new() { Name = "Reply", Exact = true }).ClickAsync();
        await Expect(Page.GetByText("does not match extension", new() { Exact = false }).First).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = fileName, Exact = false })).ToHaveCountAsync(0);
    }

    private async Task FillRichTextEditorAsync(string text)
    {
        var editor = Page.Locator("[data-testid='rich-text-editor']").Last;
        await Expect(editor).ToBeVisibleAsync();
        await editor.ClickAsync();
        await Page.Keyboard.InsertTextAsync(text);
    }
}
