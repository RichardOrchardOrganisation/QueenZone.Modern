using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Forum report page outcome handling (#2436): a missing post is a 404, and a service-level
/// rejection keeps the member on the report form with the service's message.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class ForumReportPageTests : E2EPageTest
{
    private const string MemberIdHeader = "X-Test-Member-Id";
    private const string MemberNameHeader = "X-Test-Member-Name";

    [Test]
    public async Task MissingPostReturnsNotFound()
    {
        var reporter = await CreateMemberPageAsync(Guid.NewGuid(), "E2E report missing");

        var response = await reporter.GotoAsync($"/forum/post/{int.MaxValue}/report");

        Assert.That(response?.Status, Is.EqualTo(404));

        // Leave the report route so passed-proof never captures the 404 page as report-form evidence.
        await reporter.GotoAsync("/forum");
    }

    [Test]
    public async Task UnknownReasonIsRejectedByTheServiceAndStaysOnReportForm()
    {
        var authorId = Guid.NewGuid();
        var author = await CreateMemberPageAsync(authorId, $"E2E report author {authorId:N}");
        var body = $"E2E report page body {authorId:N}";
        await author.GotoAsync("/forum/c/the-music/new-thread");
        await author.GetByLabel("Subject").FillAsync($"E2E report page {authorId:N}");
        var editor = author.Locator("[data-testid='rich-text-editor']");
        await editor.ClickAsync();
        await author.Keyboard.InsertTextAsync(body);
        await author.GetByRole(AriaRole.Button, new() { Name = "Create thread" }).ClickAsync();
        await Expect(author).ToHaveURLAsync(new Regex(".*/forum/topic/\\d+/.*"));
        var postId = (await author.Locator(".qz-forum-post").Filter(new() { HasText = body })
            .GetAttributeAsync("id"))?["post-".Length..];
        Assert.That(postId, Is.Not.Null.And.Not.Empty);

        var reporter = await CreateMemberPageAsync(Guid.NewGuid(), "E2E report page reporter");
        await reporter.GotoAsync($"/forum/post/{postId}/report");
        await Expect(reporter.GetByRole(AriaRole.Heading, new() { Name = "Report forum post" })).ToBeVisibleAsync();

        // Passes model validation (non-empty) but is not a known category, so the service decides.
        await reporter.EvaluateAsync("""
            () => {
                const select = document.querySelector("select[name='Category']");
                select.add(new Option('Unknown reason', 'not-a-real-reason'));
                select.value = 'not-a-real-reason';
            }
            """);
        await reporter.GetByRole(AriaRole.Button, new() { Name = "Submit report" }).ClickAsync();

        await Expect(reporter).ToHaveURLAsync(new Regex($".*/forum/post/{postId}/report$"));
        await Expect(reporter.GetByText("Choose a valid report reason.").First).ToBeVisibleAsync();
        await Expect(reporter.GetByRole(AriaRole.Button, new() { Name = "Submit report" })).ToBeVisibleAsync();
    }

    private async Task<IPage> CreateMemberPageAsync(Guid id, string name)
    {
        var context = await CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                [MemberIdHeader] = id.ToString(),
                [MemberNameHeader] = name,
            },
        });
        return await context.NewPageAsync();
    }
}
