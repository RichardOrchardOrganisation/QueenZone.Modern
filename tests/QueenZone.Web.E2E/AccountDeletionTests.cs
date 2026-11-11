using Microsoft.Playwright;
using QueenZone.Data;

namespace QueenZone.Web.E2E;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class AccountDeletionTests : E2EPageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        ExtraHTTPHeaders = new Dictionary<string, string>
        {
            ["X-Test-Member-Id"] = BrowserFixtureMembers.DeletionOwner.Id.ToString(),
            ["X-Test-Member-Name"] = BrowserFixtureMembers.DeletionOwner.DisplayName,
            ["X-Test-Member-Email"] = BrowserFixtureMembers.DeletionOwner.Email,
        },
    };

    [Test]
    public async Task ImmediateDeletionRequiresConfirmationAndShowsCompletedReceipt()
    {
        await Page.GotoAsync("/account/delete");
        await Page.Locator("#Confirmation").FillAsync("keep");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete my account now" }).ClickAsync();
        await Expect(Page.GetByText("Type DELETE to confirm account deletion.", new() { Exact = true })).ToBeVisibleAsync();
        await Page.Locator("#Confirmation").FillAsync("DELETE");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete my account now" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Account deletion complete" })).ToBeVisibleAsync();
        await Page.ReloadAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Account deletion complete" })).ToBeVisibleAsync();
    }
}
