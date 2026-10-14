using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public class AdminApprovalQueueProofTests : AdminFeatureProofTest
{
    [TestCase("web.admin.fanPerformanceSubmissions.index")]
    [TestCase("web.admin.newsSuggestions.index")]
    [TestCase("web.admin.photoCorrections.index")]
    [TestCase("web.admin.photoSubmissions.index")]
    [TestCase("web.admin.quizQuestionSubmissions.index")]
    [TestCase("web.admin.triviaSubmissions.index")]
    public Task Index_ProvesAnonymousMemberAndAdminAccess(string featureId) =>
        AssertAdminFeatureAsync(featureId);

    [TestCase("fanPerformanceSubmissions", "fan-performance-submissions", "fan-performance")]
    [TestCase("newsSuggestions", "news-suggestions", "news")]
    [TestCase("photoCorrections", "photo-corrections", "photo-correction/101")]
    [TestCase("photoSubmissions", "photo-submissions", "photo")]
    [TestCase("quizQuestionSubmissions", "quiz-question-submissions", "quiz-question")]
    [TestCase("triviaSubmissions", "trivia-submissions", "trivia")]
    public async Task Queue_ProvesAccessAndRejectsOnlyItsOwnSubmission(string area, string route, string submissionRoute)
    {
        var marker = NewFixtureMarker();
        var member = await CreateMemberPageAsync(marker);
        await SubmitAsync(member, submissionRoute, marker);

        var feature = $"web.admin.{area}";
        var queuePath = $"/admin/{route}";
        await AssertAdminFeatureAsync($"{feature}.index", queuePath);
        var row = Page.Locator("table.admin-table tbody tr").Filter(new() { HasText = marker });
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row).ToContainTextAsync("Pending");
        var detailPath = await row.GetByRole(AriaRole.Link, new() { Name = "Review", Exact = true }).GetAttributeAsync("href");
        Assert.That(detailPath, Does.StartWith(queuePath + "/"));
        await AssertAdminFeatureAsync($"{feature}.detail", detailPath!);
        await Expect(Page.Locator("dl").First).ToContainTextAsync(marker);
        await AssertAllAdminHandlerAccessAsync($"{feature}.action", detailPath!);

        // Checking the server-rendered details after denied POSTs also proves they did not mutate the fixture.
        await Page.ReloadAsync();
        await Expect(Status()).ToHaveTextAsync("Pending");
        var detailsBefore = await Page.Locator("dl").First.InnerTextAsync();
        var rejection = Page.Locator("form[action$='/reject']");
        var reason = $"{marker} rejected by the fixture";
        var reasonField = rejection.Locator("input[name='rejectionReason'], input[name='messageToMember'], input[name='reviewNotes']").First;
        await reasonField.FillAsync(reason);
        await rejection.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).ClickAsync();
        var dialog = rejection.Locator("dialog[open]");
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Page.ReloadAsync();
        await Expect(Page.Locator("dl").First).ToHaveTextAsync(detailsBefore);

        await reasonField.FillAsync(reason);
        await rejection.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Reject", Exact = true }).ClickAsync();
        await Expect(Status()).ToHaveTextAsync("Rejected");
        await Page.ReloadAsync();
        await Expect(Status()).ToHaveTextAsync("Rejected");
        await Expect(Page.Locator("dl").First).ToContainTextAsync(reason);
        await Expect(rejection).ToHaveCountAsync(0);
        await Page.GotoAsync(queuePath);
        await Expect(row).ToHaveCountAsync(0);

        // End on the detail surface so the uploaded proof screenshot covers the detail and action entries.
        await Page.GotoAsync(detailPath!);
        await Expect(Status()).ToHaveTextAsync("Rejected");
    }

    private ILocator Status() => Page.Locator("dl").First.Locator("dt").Filter(new() { HasTextRegex = new Regex("^Status$") }).Locator("+ dd");

    private async Task<IPage> CreateMemberPageAsync(string marker)
    {
        var context = await CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["X-Test-Member-Id"] = Guid.NewGuid().ToString(),
                ["X-Test-Member-Name"] = marker,
                ["X-Test-Member-Email"] = $"{marker}@test.local"
            }
        });
        return await context.NewPageAsync();
    }

    private async Task SubmitAsync(IPage member, string route, string marker)
    {
        var response = await member.GotoAsync($"/submit/{route}");
        Assert.That(response?.Status, Is.EqualTo(200));
        switch (route)
        {
            case "news":
                await member.GetByLabel("News story URL").FillAsync($"https://example.com/{marker}");
                await member.GetByLabel("Suggested headline").FillAsync(marker);
                break;
            case "trivia":
                await member.GetByLabel("Trivia fact").FillAsync($"{marker} Queen trivia fact.");
                break;
            case "quiz-question":
                await member.Locator("#QuestionText").FillAsync($"{marker} Which band recorded Bohemian Rhapsody?");
                await member.Locator("#SourceNote").FillAsync(marker);
                await member.Locator("input[name='OptionTexts[0]']").FillAsync("Queen");
                await member.Locator("input[name='OptionTexts[1]']").FillAsync("The Beatles");
                await member.Locator("input[name='CorrectOptionIndex'][value='0']").CheckAsync();
                break;
            case "photo-correction/101":
                // Read the sample photo, but only reject our new proposal; never edit the sample itself.
                await member.Locator("#ProposedValue").FillAsync(marker);
                await member.Locator("#Explanation").FillAsync("Disposable fixture proposal for queue verification.");
                break;
            case "photo":
                await member.GetByLabel("Title", new() { Exact = true }).FillAsync(marker);
                using (var image = new Image<Rgba32>(200, 200, new Rgba32(100, 149, 237, 255)))
                using (var stream = new MemoryStream())
                {
                    image.SaveAsPng(stream);
                    await member.Locator("#PhotoFile").SetInputFilesAsync(new FilePayload
                    {
                        Name = $"{marker}.png",
                        MimeType = "image/png",
                        Buffer = stream.ToArray()
                    });
                }
                break;
            case "fan-performance":
                await member.GetByLabel("Title", new() { Exact = true }).FillAsync(marker);
                await member.GetByLabel("Queen song covered").FillAsync("Love of My Life");
                await member.GetByLabel("Performed by").FillAsync(marker);
                // Same MPEG-1 Layer III fixture shape used by CommunitySubmissionWorkflowTests.
                var audio = new byte[400];
                audio[0] = 0xFF;
                audio[1] = 0xFB;
                audio[2] = 0x90;
                await member.Locator("#AudioFile").SetInputFilesAsync(new FilePayload
                {
                    Name = $"{marker}.mp3",
                    MimeType = "audio/mpeg",
                    Buffer = audio
                });
                await member.Locator("#RightsDeclarationAccepted").CheckAsync();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(route));
        }

        var button = route switch
        {
            "news" => "Submit suggestion",
            _ when route.StartsWith("photo-correction/", StringComparison.Ordinal) => "Send for review",
            _ => "Submit for review"
        };
        await member.GetByRole(AriaRole.Button, new() { Name = button, Exact = true }).ClickAsync();
        var confirmationRoute = route.Split('/')[0];
        try
        {
            await Expect(member).ToHaveURLAsync(new Regex($"/submit/{confirmationRoute}/confirmation(?:/|$)"));
        }
        catch (PlaywrightException)
        {
            var validationMessages = await member.Locator("[data-valmsg-summary], .validation-summary-errors, .text-danger").AllInnerTextsAsync();
            TestContext.WriteLine($"Submission confirmation was not reached. Current URL: {member.Url}. Validation: {string.Join(" | ", validationMessages)}");
            throw;
        }
    }
}
