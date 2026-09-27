using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Deterministic PR-gate hard checks for the curated high-value page set (#1597):
/// 390px overflow, visible encoding artifacts, and skip-link keyboard access.
/// The sampled live-site sitemap sweep stays soft for overflow and encoding.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class CuratedPageLayoutSmokeTests : E2EPageTest
{
    private const string TestMemberIdHeader = "X-Test-Member-Id";
    private const string TestMemberNameHeader = "X-Test-Member-Name";

    [TestCaseSource(typeof(CuratedLayoutPages), nameof(CuratedLayoutPages.Public))]
    public async Task PublicPage_HasNoOverflowEncodingOrKeyboardTrap(CuratedLayoutPage page)
    {
        var browserPage = await OpenPhonePageAsync(member: false);
        await AssertCuratedPageAsync(browserPage, page);
    }

    [TestCaseSource(typeof(CuratedLayoutPages), nameof(CuratedLayoutPages.Member))]
    public async Task MemberPage_HasNoOverflowEncodingOrKeyboardTrap(CuratedLayoutPage page)
    {
        var browserPage = await OpenPhonePageAsync(member: true);
        await AssertCuratedPageAsync(browserPage, page);
    }

    private async Task<IPage> OpenPhonePageAsync(bool member)
    {
        var options = new BrowserNewContextOptions
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize
            {
                Width = CuratedLayoutPages.PhoneWidth,
                Height = CuratedLayoutPages.PhoneHeight,
            },
        };

        if (member)
        {
            options.ExtraHTTPHeaders = new Dictionary<string, string>
            {
                [TestMemberIdHeader] = Guid.NewGuid().ToString(),
                [TestMemberNameHeader] = "Playwright Layout Fan",
            };
        }

        var context = await CreateExtraContextAsync(options);
        return await context.NewPageAsync();
    }

    private static async Task AssertCuratedPageAsync(IPage browserPage, CuratedLayoutPage page)
    {
        var response = await browserPage.GotoAsync(page.Path);
        Assert.That(response?.Status, Is.EqualTo(200), $"Expected HTTP 200 for {page.Path}.");

        await Assertions.Expect(browserPage.GetByRole(AriaRole.Heading, new()
        {
            Name = page.Heading,
            Level = 1
        })).ToBeVisibleAsync();

        await PageShapeAssertions.AssertNoHorizontalOverflowAsync(browserPage, page.Path);
        await PageShapeAssertions.AssertNoEncodingArtifactsAsync(browserPage);
        await KeyboardAssertions.AssertSkipLinkMovesFocusToMainAsync(browserPage);
    }
}
