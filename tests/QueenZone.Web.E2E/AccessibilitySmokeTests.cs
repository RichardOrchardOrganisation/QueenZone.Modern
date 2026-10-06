using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// axe-core smoke for curated PR-gate pages: critical always fails; serious
/// fails unless <see cref="AxeSeriousExceptions"/> lists a triaged legacy pair.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
[Category(E2ECategories.ReadOnly)]
public class AccessibilitySmokeTests : E2EPageTest
{
    [Test]
    public async Task Homepage_SkipLink_MovesFocusToMainContent()
    {
        await Page.GotoAsync("/");
        await Expect(Page.GetByText("Latest news")).ToBeVisibleAsync();

        await KeyboardAssertions.AssertSkipLinkMovesFocusToMainAsync(Page);
    }

    [Test]
    public async Task Homepage_HasNoBlockingAxeViolations()
    {
        await Page.GotoAsync("/");
        await Expect(Page.GetByText("Latest news")).ToBeVisibleAsync();

        await AxeAssertions.AssertNoBlockingViolationsAsync(Page, "/");
    }

    [Test]
    public async Task NewsDetail_HasNoBlockingAxeViolations()
    {
        await Page.GotoAsync("/news/1003/queenzone-modernisation-begins");
        await Expect(Page.GetByRole(AriaRole.Heading, new()
        {
            Name = "QueenZone modernisation begins",
            Level = 1
        })).ToBeVisibleAsync();

        await AxeAssertions.AssertNoBlockingViolationsAsync(
            Page,
            "/news/1003/queenzone-modernisation-begins");
    }

    [Test]
    public async Task ForumTopic_HasNoBlockingAxeViolations()
    {
        await Page.GotoAsync("/forum/topic/1002/ranking-every-studio-album");
        await Expect(Page.GetByRole(AriaRole.Heading, new()
        {
            Name = "Ranking every studio album",
            Level = 1
        })).ToBeVisibleAsync();

        await AxeAssertions.AssertNoBlockingViolationsAsync(
            Page,
            "/forum/topic/1002/ranking-every-studio-album");
    }
}
