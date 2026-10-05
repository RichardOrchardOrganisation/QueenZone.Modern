using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Public visitor browser smoke: homepage, news, forum, and related archive surfaces.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class SmokeTests : E2EPageTest
{
    private readonly List<string> _consoleErrors = [];

    // Runs for every test in this fixture (deterministic app, so no mirror-data 404 noise is
    // expected). Promotes the nightly sitemap sweep's console-error check into the PR gate so a
    // regression is caught before merge instead of the following night.
    [SetUp]
    public void SubscribeConsoleErrors()
    {
        _consoleErrors.Clear();
        Page.Console += OnConsoleMessage;
    }

    [TearDown]
    public void AssertNoActionableConsoleErrors()
    {
        Page.Console -= OnConsoleMessage;
        var actionable = _consoleErrors.Where(PageShapeAssertions.IsActionableConsoleError).ToList();
        Assert.That(
            actionable,
            Is.Empty,
            "Unexpected browser console error(s): " + string.Join(" | ", actionable));
    }

    private void OnConsoleMessage(object? _, IConsoleMessage message)
    {
        if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
        {
            _consoleErrors.Add(message.Text);
        }
    }

    private Task AssertNoEncodingArtifactsAsync() =>
        PageShapeAssertions.AssertNoEncodingArtifactsAsync(Page);

    [Test]
    public async Task Homepage_ShowsLatestNews()
    {
        await Page.GotoAsync("/");

        await Expect(Page.GetByText("Latest news")).ToBeVisibleAsync();
        await Expect(Page.Locator(".qz-home-archive-links a[href='/news']")).ToBeVisibleAsync();
        await Expect(Page.Locator("[data-testid='env-banner']")).ToHaveTextAsync("LOCAL");

        await AssertNoEncodingArtifactsAsync();
    }

    [Test]
    public async Task NewsArchive_ShowsArchiveHeading()
    {
        await Page.GotoAsync("/news");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "News", Level = 1 })).ToBeVisibleAsync();
    }

    [Test]
    public async Task NewsArchive_Pagination_NavigatesToNextPage()
    {
        await Page.GotoAsync("/news");

        var pageOneSummary = await Page.Locator(".archive-pagination-summary").First.InnerTextAsync();
        Assert.That(pageOneSummary, Does.Contain("Page 1"));

        var firstTitle = await Page.Locator(".qz-news-row a").First.InnerTextAsync();

        await Page.Locator("a.archive-pagination-next").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/news/page/2/?$"));

        var pageTwoSummary = await Page.Locator(".archive-pagination-summary").First.InnerTextAsync();
        Assert.That(pageTwoSummary, Does.Contain("Page 2"));

        var secondTitle = await Page.Locator(".qz-news-row a").First.InnerTextAsync();
        Assert.That(secondTitle, Is.Not.EqualTo(firstTitle));
    }

    [Test]
    public async Task NewsDetail_ShowsCanonicalAndBody()
    {
        await Page.GotoAsync("/news/1003/queenzone-modernisation-begins");

        await Expect(Page.GetByRole(AriaRole.Heading, new()
        {
            Name = "QueenZone modernisation begins",
            Level = 1
        })).ToBeVisibleAsync();

        await Expect(Page.Locator("article.article-body")).ToBeVisibleAsync();
        await Expect(Page.Locator("article.article-body")).ToContainTextAsync("ASP.NET Core");
        await Expect(Page.Locator("article.article-body")).ToContainTextAsync("news archive");
        await Expect(Page.GetByRole(AriaRole.Img, new() { Name = "QueenZone crest" })).ToBeVisibleAsync();

        var canonical = Page.Locator("link[rel='canonical']");
        await Expect(canonical).ToHaveCountAsync(1);
        var href = await canonical.GetAttributeAsync("href");
        Assert.That(href, Does.Contain("/news/1003/queenzone-modernisation-begins"));

        await AssertNoEncodingArtifactsAsync();
    }

    [Test]
    public async Task ForumIndex_ShowsBoards()
    {
        await Page.GotoAsync("/forum");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Forum", Level = 1 })).ToBeVisibleAsync();
        // Board cards and the recent-threads table both link to categories; target the card.
        await Expect(Page.Locator("a.qz-card[href='/forum/1/the-music']")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Latest activity across boards", Level = 2 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Ranking every studio album", Exact = true })).ToBeVisibleAsync();
    }

    [Test]
    public async Task ForumCategory_ListsTopics()
    {
        await Page.GotoAsync("/forum/1/the-music");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "The Music", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Ranking every studio album" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task ForumTopic_ShowsPostsAndBreadcrumbs()
    {
        await Page.GotoAsync("/forum/topic/1002/ranking-every-studio-album");

        await Expect(Page.GetByRole(AriaRole.Heading, new()
        {
            Name = "Ranking every studio album",
            Level = 1
        })).ToBeVisibleAsync();

        var breadcrumbs = Page.GetByRole(AriaRole.Navigation, new() { Name = "Breadcrumb" });
        await Expect(breadcrumbs).ToBeVisibleAsync();
        await Expect(breadcrumbs.GetByRole(AriaRole.Link, new() { Name = "Forum", Exact = true })).ToBeVisibleAsync();

        // Sample seed posts include discussion content for this topic.
        await Expect(Page.Locator(".qz-forum-posts")).ToBeVisibleAsync();
        await Expect(Page.Locator(".qz-forum-post").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task ArticlesArchive_ShowsHeading()
    {
        await Page.GotoAsync("/articles");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Articles", Level = 1 })).ToBeVisibleAsync();
    }

    [Test]
    public async Task BiographyIndex_ShowsHeading()
    {
        await Page.GotoAsync("/biography");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Biography", Level = 1 })).ToBeVisibleAsync();
    }

    [Test]
    public async Task PhotographyIndex_ShowsHeading()
    {
        await Page.GotoAsync("/photography");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Photography", Level = 1 })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Search_ShowsSearchForm()
    {
        await Page.GotoAsync("/search");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Search", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.Locator("#qz-search")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Search_SubmitsKnownQuery_PersistsFilterAndOpensResult()
    {
        await Page.GotoAsync("/search");

        await Page.Locator("#qz-search").FillAsync("modernisation");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@".*/search\?q=modernisation$"));
        await Expect(Page.Locator("#qz-search")).ToHaveValueAsync("modernisation");

        var result = Page.GetByRole(AriaRole.Link, new() { Name = "QueenZone modernisation begins" });
        await Expect(result).ToBeVisibleAsync();

        var filters = Page.GetByRole(AriaRole.Navigation, new() { Name = "Filter search results by content type" });
        await Expect(filters).ToBeVisibleAsync();
        await filters.GetByRole(AriaRole.Link, new() { Name = "News", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@".*/search\?q=modernisation&type=news$"));
        await Expect(Page.Locator("#qz-search")).ToHaveValueAsync("modernisation");
        await Expect(result).ToBeVisibleAsync();

        await result.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(".*/news/1003/queenzone-modernisation-begins/?$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new()
        {
            Name = "QueenZone modernisation begins",
            Level = 1
        })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Search_UnknownQuery_ShowsNoResults()
    {
        await Page.GotoAsync("/search");

        await Page.Locator("#qz-search").FillAsync("volcano-xyz-no-match");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@".*/search\?q=volcano-xyz-no-match$"));
        await Expect(Page.Locator("#qz-search")).ToHaveValueAsync("volcano-xyz-no-match");
        await Expect(Page.GetByText("No results found for")).ToBeVisibleAsync();
        await Expect(Page.GetByText("volcano-xyz-no-match")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Homepage_RendersOnMobileViewport()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize
            {
                Width = CuratedLayoutPages.PhoneWidth,
                Height = CuratedLayoutPages.PhoneHeight,
            },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/");

        await Expect(page.GetByText("Latest news")).ToBeVisibleAsync();

        await PageShapeAssertions.AssertNoHorizontalOverflowAsync(page, "/");
    }

    [Test]
    public async Task QuizSprint_UsesNativeCountdownProgress()
    {
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
            admin.Dialog += (_, dialog) => dialog.AcceptAsync();
            await quiz.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        }
    }

    [Test]
    public async Task MobileViewport_OpensNavigationMenu()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize
            {
                Width = CuratedLayoutPages.PhoneWidth,
                Height = CuratedLayoutPages.PhoneHeight,
            },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync("/");
        await page.Locator("[data-menu-open]").ClickAsync();

        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Primary navigation" });
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.GetByRole(AriaRole.Link, new() { Name = "News", Exact = true })).ToBeVisibleAsync();
        Assert.That(await dialog.EvaluateAsync<bool>("element => element instanceof HTMLDialogElement && element.matches(':modal')"), Is.True);
        await page.ScreenshotAsync(new() { Path = System.IO.Path.Combine(E2EArtifactPaths.EnsureDirectory(), "mobile-navigation-native-dialog.png"), FullPage = true });
        await page.Keyboard.PressAsync("Escape");
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.Locator("[data-menu-open]")).ToBeFocusedAsync();
        await page.Locator("[data-menu-open]").ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Close navigation menu" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(page.Locator("[data-menu-open]")).ToBeFocusedAsync();
    }
}
