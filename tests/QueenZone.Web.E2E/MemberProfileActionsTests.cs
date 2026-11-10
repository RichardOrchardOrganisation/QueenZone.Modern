using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QueenZone.Data;

namespace QueenZone.Web.E2E;

/// <summary>
/// Member profile follow and block outcome handling (#2436). Uses the Testing host's
/// <see cref="BrowserFixtureMembers"/> (MemberBrowserFixture:Enabled) so the profile exists.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category(E2ECategories.Deterministic)]
public class MemberProfileActionsTests : E2EPageTest
{
    private static readonly BrowserFixtureMember Viewer = BrowserFixtureMembers.ProfileViewer;
    private static readonly BrowserFixtureMember Subject = BrowserFixtureMembers.ProfileSubject;

    public override BrowserNewContextOptions ContextOptions() =>
        new()
        {
            BaseURL = BaseUrl,
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["X-Test-Member-Id"] = Viewer.Id.ToString(),
                ["X-Test-Member-Name"] = Viewer.DisplayName,
                ["X-Test-Member-Email"] = Viewer.Email,
            },
        };

    [Test]
    public async Task FollowAndBlockShowStatusOnTheProfile()
    {
        var profilePath = $"/members/{Subject.Id}";
        await Page.GotoAsync(profilePath);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = Subject.DisplayName, Level = 1 })).ToBeVisibleAsync();
        await ResetRelationshipAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Follow", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($".*{Regex.Escape(profilePath)}$"));
        await Expect(Page.GetByText("You are now following this member.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Unfollow" })).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Block", Exact = true }).ClickAsync();
        await Page.Locator("dialog[open]").GetByRole(AriaRole.Button, new() { Name = "Block", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex($".*{Regex.Escape(profilePath)}$"));
        await Expect(Page.GetByText("Member blocked. They can no longer send you private messages.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Unblock" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task FollowingAMissingMemberReturnsNotFound()
    {
        await Page.GotoAsync($"/members/{Subject.Id}");
        await ResetRelationshipAsync();
        var missing = Guid.NewGuid();

        // Point the real follow form (with its antiforgery token) at a member that does not exist.
        var response = await Page.RunAndWaitForNavigationAsync(
            () => Page.EvaluateAsync("""
                id => {
                    const form = Array.from(document.forms).find(f => f.action.includes('handler=Follow'));
                    form.action = form.action.replace(/\/members\/[^/?]+/, '/members/' + id);
                    form.requestSubmit();
                }
                """, missing.ToString()),
            new() { UrlFunc = url => url.Contains(missing.ToString(), StringComparison.Ordinal) });

        Assert.That(response?.Status, Is.EqualTo(404));

        // Leave the profile route so passed-proof never captures the 404 page as profile evidence.
        await Page.GotoAsync("/forum");
    }

    private async Task ResetRelationshipAsync()
    {
        // Re-runs against a kept-alive host start from a clean follow/block state.
        var unblock = Page.GetByRole(AriaRole.Button, new() { Name = "Unblock" });
        if (await unblock.CountAsync() > 0)
        {
            await unblock.ClickAsync();
            await Expect(Page.GetByText("Member unblocked.")).ToBeVisibleAsync();
        }

        var unfollow = Page.GetByRole(AriaRole.Button, new() { Name = "Unfollow" });
        if (await unfollow.CountAsync() > 0)
        {
            await unfollow.ClickAsync();
            await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Follow", Exact = true })).ToBeVisibleAsync();
        }
    }
}
