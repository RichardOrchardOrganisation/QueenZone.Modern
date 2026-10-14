using Microsoft.Playwright;

namespace QueenZone.Web.E2E;

[TestFixture]
[Category(E2ECategories.Deterministic)]
public class AdminMembersProofTests : AdminMemberContentProofTest
{
    [Test]
    public Task Index_ProvesAnonymousMemberAndAdminAccess() =>
        AssertAdminFeatureAsync("web.admin.members.index", "/admin/members");

    [TestCase("Suspend")]
    [TestCase("Reinstate")]
    [TestCase("HideForumContent")]
    [TestCase("UnhideForumContent")]
    public Task MemberHandlers_RejectUnauthorisedRequests(string handler) =>
        AssertPostAccessAsync($"/admin/members/{Guid.NewGuid()}/{handler}");

    [Test]
    public async Task MemberReview_ProvesDetailAndCancelsHideWithoutMutation()
    {
        var name = FixturePrefix + Guid.NewGuid().ToString("N");
        await Page.GotoAsync("/admin/reviewer-accounts");
        await Page.Locator("#Input_Email").FillAsync(name + "@example.com");
        await Page.Locator("#Input_DisplayName").FillAsync(name);
        var password = Guid.NewGuid().ToString("N") + "aA1!";
        await Page.Locator("#Input_Password").FillAsync(password);
        await Page.Locator("#Input_ConfirmPassword").FillAsync(password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Add account" }).ClickAsync();
        await AssertAdminFeatureAsync("web.admin.members.index", "/admin/members?query=" + name);
        var row = Page.GetByRole(AriaRole.Row).Filter(new() { HasText = name });
        await Expect(row).ToHaveCountAsync(1);
        await Expect(row).ToContainTextAsync("Active");
        var review = row.GetByRole(AriaRole.Link, new() { Name = "Review" });
        var path = (await review.GetAttributeAsync("href"))!;
        var id = Guid.Parse(path.Split('/')[^1]);
        var topic = await CreateTopicAsync(id, name);
        await review.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true })).ToBeVisibleAsync();
        await AssertAdminFeatureAsync("web.admin.members.detail", path);
        await Expect(Page.Locator(".admin-detail-list")).ToContainTextAsync(name + "@example.com");
        await Expect(Page.Locator(".admin-detail-list")).ToContainTextAsync("Active");
        await Expect(Page.GetByText("1 posts and 1 started threads match this author.", new() { Exact = true })).ToBeVisibleAsync();
        var suspend = Page.Locator($"form[action='{path}/Suspend']");
        await Expect(suspend.Locator("textarea[name=Reason]")).ToHaveAttributeAsync("required", "");
        await Expect(suspend.GetByRole(AriaRole.Button, new() { Name = "Suspend member" })).ToBeVisibleAsync();
        await AssertAdminFeatureAsync("web.admin.members.action", path);
        await ProveCancelAsync($"form[action='{path}/HideForumContent']", "member-hide-confirmation");
        await Expect(Page.Locator(".admin-detail-list")).ToContainTextAsync("Active");
        await Expect(Page.GetByText("1 posts and 1 started threads match this author.", new() { Exact = true })).ToBeVisibleAsync();
        await topic.ReloadAsync();
        await Expect(topic.Locator(".qz-forum-post").Filter(new() { HasText = name })).ToBeVisibleAsync();
    }

}
