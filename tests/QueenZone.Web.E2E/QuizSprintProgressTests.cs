using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public sealed class QuizSprintProgressTests : E2EPageTest
{
    [Test]
    public async Task QuizSprint_UsesNativeCountdownProgress()
    {
        var consoleErrors = new List<string>();
        Page.Console += (_, message) =>
        {
            if (message.Type == "error") consoleErrors.Add(message.Text);
        };
        var adminContext = await CreateExtraContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string> { ["X-Test-User-Email"] = "admin@test.local" },
        });
        var admin = await adminContext.NewPageAsync();
        var title = "Native progress proof " + Guid.NewGuid().ToString("N");
        await admin.GotoAsync("/admin/quizzes/new");
        await admin.GetByLabel("Title", new() { Exact = true }).FillAsync(title);
        var questions = admin.Locator("[data-quiz-question]");
        for (var index = 0; index < 3; index++)
        {
            var question = questions.Nth(index);
            await question.GetByLabel("Question text", new() { Exact = true }).FillAsync($"Queen question {index + 1}");
            await question.GetByLabel("Option 1", new() { Exact = true }).FillAsync("Freddie Mercury");
            await question.GetByLabel("Option 2", new() { Exact = true }).FillAsync("Brian May");
            await question.GetByRole(AriaRole.Radio).First.CheckAsync();
        }
        await admin.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await admin.GotoAsync("/admin/quizzes");
        var quiz = admin.GetByRole(AriaRole.Row).Filter(new() { HasText = title });
        try
        {
            await quiz.GetByRole(AriaRole.Button, new() { Name = "Publish", Exact = true }).ClickAsync();
            await Page.GotoAsync("/quizzes/sprint");
            await Page.GetByRole(AriaRole.Button, new() { Name = "Begin the sprint", Exact = true }).ClickAsync();
            var progress = Page.GetByRole(AriaRole.Progressbar, new() { Name = "Time remaining" });
            await Expect(progress).ToBeVisibleAsync();
            await Expect(progress).ToHaveAttributeAsync("max", "60");
            Assert.That(await progress.EvaluateAsync<bool>("element => element instanceof HTMLProgressElement"), Is.True);
            var initial = await progress.EvaluateAsync<double>("element => element.value");
            Assert.That(initial, Is.InRange(0.0, 60.0));
            await Page.WaitForFunctionAsync("initial => document.querySelector('[data-sprint-progress]').value < initial", initial);
            await Expect(progress).ToHaveAttributeAsync("aria-valuetext", new System.Text.RegularExpressions.Regex("^\\d+ seconds remaining$"));
            // WebKit screenshot preparation injects an inline style blocked by the enforced CSP.
            // Keep the gameplay checks under CSP on every browser; capture this proof in Chromium.
            if (BrowserType.Name == "chromium")
            {
                await Page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(E2EArtifactPaths.EnsureDirectory(), "quiz-sprint-native-progress.png"), FullPage = true });
            }
        }
        finally
        {
            await admin.GotoAsync("/admin/quizzes");
            await quiz.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
            await admin.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        }
        Assert.That(consoleErrors.Where(PageShapeAssertions.IsActionableConsoleError), Is.Empty,
            "Unexpected browser console errors during the countdown.");
    }
}
