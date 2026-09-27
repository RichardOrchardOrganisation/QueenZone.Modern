using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

/// <summary>
/// Phone-viewport coverage for private messaging surfaces (#474). Uses the Testing
/// environment's member test headers so the inbox/compose forms are reachable without
/// the SQL Express mirror.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class PrivateMessagingMobileTests : E2EPageTest
{
    private const string TestMemberIdHeader = "X-Test-Member-Id";
    private const string TestMemberNameHeader = "X-Test-Member-Name";
    private const int PhoneWidth = CuratedLayoutPages.PhoneWidth;
    private const int PhoneHeight = CuratedLayoutPages.PhoneHeight;

    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            BaseURL = BaseUrl,
            ViewportSize = new ViewportSize { Width = PhoneWidth, Height = PhoneHeight },
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                [TestMemberIdHeader] = Guid.NewGuid().ToString(),
                [TestMemberNameHeader] = "Playwright Messages Fan",
            }
        };

    [Test]
    public async Task MobileHeader_ShowsMessagesControlWithoutOpeningMenu()
    {
        await Page.GotoAsync("/messages");

        var messages = Page.GetByRole(AriaRole.Link, new() { Name = "Messages" });
        await Expect(messages.First).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "New message" })).ToBeVisibleAsync();
        await ExpectNoHorizontalOverflowAsync();
    }

    [Test]
    public async Task MobileCompose_AndReplyForms_StayUsable()
    {
        await Page.GotoAsync("/messages/compose");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "New message", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByLabel("To")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Find member" })).ToBeVisibleAsync();
        await ExpectNoHorizontalOverflowAsync();
    }

    private Task ExpectNoHorizontalOverflowAsync() =>
        PageShapeAssertions.AssertNoHorizontalOverflowAsync(Page, Page.Url);
}
