using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework.Interfaces;
using QueenZone.Routing;

namespace QueenZone.Web.E2E;

/// <summary>
/// High-signal Playwright journey against the deployed DEV app
/// (<c>https://dev.queenzone.org</c>). Discovery-based: no Testing seed ids
/// such as <c>/news/1003/…</c> or <c>/forum/topic/1002/…</c>.
/// <para>
/// Write coverage is skipped for Ship A. The curated DEV snapshot is shared
/// across tip runs and <c>refresh-dev-snapshot</c>; a forum or private-message
/// write would race other tip gates and pollute the snapshot. Nightly RealData
/// keeps write probes. Revisit only with an idempotent, uniquely-prefixed probe
/// that self-deletes.
/// </para>
/// <para>
/// Password / artifact rule: no Playwright trace or video (those could capture
/// the password form). Failure screenshots are taken only after sign-in, on
/// journey pages.
/// </para>
/// <para>
/// Sign-in is once per fixture (<c>[OneTimeSetUp]</c>). The shared helper
/// captures Playwright storage state after that single password submit, and
/// each test context is created from it. Each test <c>[SetUp]</c> opens
/// <c>/</c> and hard-expects the Sign out button. If it is missing, the
/// fixture fails with a clear message instead of signing in again.
/// </para>
/// </summary>
[TestFixture]
[Category(E2ECategories.DevJourney)]
public class DevJourneyTests : PageTest
{
    private static string? _signedInStorageState;
    private static bool _fixtureSignedIn;

    public override BrowserNewContextOptions ContextOptions()
    {
        Assert.That(
            _signedInStorageState,
            Is.Not.Null.And.Not.Empty,
            DeployedMemberSignIn.SignedOutMessage);
        return new BrowserNewContextOptions
        {
            BaseURL = DeployedAuthTarget.RequireDevUrl(Environment.GetEnvironmentVariable("E2E_BASE_URL")).ToString(),
            StorageState = _signedInStorageState,
        };
    }

    [OneTimeSetUp]
    public async Task SignInOnceForFixtureAsync()
    {
        _fixtureSignedIn = false;
        _signedInStorageState = await DeployedMemberSignIn.CaptureSignedInStorageStateAsync(
            Environment.GetEnvironmentVariable("E2E_BASE_URL"));
        _fixtureSignedIn = true;
    }

    [SetUp]
    public async Task AssertFixtureSessionStillActiveAsync()
    {
        await DeployedMemberSignIn.AssertSignedInChromeAsync(Page);
    }

    [TearDown]
    public async Task CaptureJourneyFailureScreenshotAsync()
    {
        if (!_fixtureSignedIn)
        {
            return;
        }

        var outcome = TestContext.CurrentContext.Result.Outcome.Status;
        if (outcome is not (TestStatus.Failed or TestStatus.Warning))
        {
            return;
        }

        var dir = E2EArtifactPaths.EnsureDirectory();
        var name = E2EArtifactPaths.SanitizeFileName(TestContext.CurrentContext.Test.Name);
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        try
        {
            await Page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(dir, $"{name}-{stamp}.png"),
                FullPage = true
            });
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"Journey screenshot capture failed: {ex.Message}");
        }
    }

    [Test]
    public async Task Home_ReturnsOkWithSingleHeadingAndNonLocalBanner()
    {
        var response = await Page.GotoAsync("/");
        Assert.That(response, Is.Not.Null, "Expected a navigation response for /.");
        Assert.That(response!.Status, Is.EqualTo(200));

        var headings = Page.Locator("h1");
        await Expect(headings).ToHaveCountAsync(1);
        var headingText = (await headings.InnerTextAsync()).Trim();
        Assert.That(headingText, Is.Not.Empty, "Home h1 must be non-empty.");

        await Expect(Page.GetByText("Latest news")).ToBeVisibleAsync();

        var banner = Page.Locator("[data-testid='env-banner']");
        await Expect(banner).ToBeVisibleAsync();
        var bannerText = (await banner.InnerTextAsync()).Trim();
        Assert.That(
            bannerText,
            Is.Not.EqualTo("LOCAL").And.Not.Empty,
            "Deployed DEV must not show the Testing host LOCAL banner.");
    }

    [Test]
    public async Task Search_SubmitsWordDiscoveredFromLiveDevContent()
    {
        await Page.GotoAsync("/");
        var title = await Page.Locator(".qz-home-newslist__title").First.InnerTextAsync();
        var word = DevJourneyDiscovery.FirstSearchWord(title);

        await Page.GotoAsync("/search");
        await Page.Locator("#qz-search").FillAsync(word);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex(@".*/search\?q="));
        await Expect(Page.Locator("#qz-search")).ToHaveValueAsync(word);

        var noResults = Page.GetByText("No results found for");
        var resultTitle = Page.Locator("a.qz-card h3");
        Assert.That(
            await noResults.CountAsync() + await resultTitle.CountAsync(),
            Is.GreaterThan(0),
            "Search must show results or a coherent no-results page.");

        if (await noResults.CountAsync() > 0)
        {
            await Expect(noResults).ToBeVisibleAsync();
            await Expect(Page.GetByText(word)).ToBeVisibleAsync();
        }
        else
        {
            await Expect(resultTitle.First).ToBeVisibleAsync();
        }
    }

    [Test]
    public async Task Forum_OpensFirstRealTopicWithPosts()
    {
        await Page.GotoAsync("/forum");
        var topicLink = Page.Locator("a[href*='/forum/topic/']").First;
        await Expect(topicLink).ToBeVisibleAsync();
        await topicLink.ClickAsync();

        var heading = Page.Locator("h1");
        await Expect(heading).ToHaveCountAsync(1);
        Assert.That((await heading.InnerTextAsync()).Trim(), Is.Not.Empty, "Forum topic h1 must be non-empty.");
        await Expect(Page.Locator(".qz-forum-post").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Articles_ShowsHeadingAndArchiveRow()
    {
        await Page.GotoAsync("/articles");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Articles", Level = 1 }))
            .ToBeVisibleAsync();
        await Expect(Page.Locator(".qz-news-row a").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Discography_ShowsHeadingAndAlbumLink()
    {
        await Page.GotoAsync(DiscographyRoutes.GetIndexPath());
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Discography", Level = 1 }))
            .ToBeVisibleAsync();
        await Expect(Page.Locator("a.qz-album-card").First).ToBeVisibleAsync();
    }
}
