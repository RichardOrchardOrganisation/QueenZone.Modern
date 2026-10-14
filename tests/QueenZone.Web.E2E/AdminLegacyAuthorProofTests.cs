using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public class AdminLegacyAuthorProofTests : AdminMemberContentProofTest
{
    [TestCase("Hide")]
    [TestCase("Unhide")]
    public Task LegacyAuthorHandlers_RejectUnauthorisedRequests(string handler) =>
        AssertPostAccessAsync($"/admin/members/forum-author/{handler}");

    [Test]
    public async Task LegacyAuthor_ProvesConfirmationAndCancelsWithoutMutation()
    {
        var name = FixturePrefix + Guid.NewGuid().ToString("N");
        var topic = await CreateTopicAsync(Guid.NewGuid(), name);
        await AssertAdminFeatureAsync("web.admin.members.forumAuthorAction", "/admin/members?query=" + name);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByText("No member account.", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(Page.Locator("input[name=DisplayName]")).ToHaveValueAsync(name);
        await ProveCancelAsync("form[action='/admin/members/forum-author/Hide']", "legacy-hide-confirmation");
        await Expect(Page.GetByText("1 posts and 1 started threads", new() { Exact = false })).ToBeVisibleAsync();
        await topic.ReloadAsync();
        await Expect(topic.Locator(".qz-forum-post").Filter(new() { HasText = name })).ToBeVisibleAsync();
    }

}
