using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class HomePollIndexRoutesTests : IClassFixture<WebHostVariantCache>, IAsyncLifetime
{
    private readonly VariantWebApplicationFactory factory;

    public HomePollIndexRoutesTests(WebHostVariantCache variants)
    {
        factory = variants.Get(WebHostVariants.IsolatedHomePolls);
    }

    public Task InitializeAsync() => factory.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Index_omits_the_poll_section_when_none_is_current()
    {
        using var client = factory.CreateAnonymousClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("id=\"home-poll\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("QueenZone poll", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guest_sees_results_and_member_can_vote_once_from_index()
    {
        var optionId = await PublishPollAsync(factory, "Index poll?", ["Alpha", "Beta"]);
        using var guest = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var guestHtml = await guest.GetStringAsync("/");
        Assert.Contains("Index poll?", guestHtml, StringComparison.Ordinal);
        Assert.Contains("Sign in to vote", guestHtml, StringComparison.Ordinal);
        Assert.Contains("0 · 0%", guestHtml, StringComparison.Ordinal);

        using var guestVote = await guest.PostAsync("/?handler=Vote", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["optionId"] = optionId.ToString(),
        }));
        Assert.True(
            guestVote.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest,
            $"Guest vote should be rejected, got {guestVote.StatusCode}.");

        var memberId = Guid.NewGuid();
        using var member = factory.CreateAnonymousClient(allowAutoRedirect: false);
        member.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, memberId.ToString());
        member.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Home Voter");

        var home = await member.GetStringAsync("/");
        var token = AdminHttpTestHelpers.ExtractAntiforgeryToken(home);
        using var first = await member.PostAsync("/?handler=Vote", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["optionId"] = optionId.ToString(),
        }));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Contains("#home-poll", first.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var after = await member.GetStringAsync("/");
        Assert.Contains("Your vote", after, StringComparison.Ordinal);
        Assert.Contains("1 · 100%", after, StringComparison.Ordinal);

        token = AdminHttpTestHelpers.ExtractAntiforgeryToken(after);
        using var second = await member.PostAsync("/?handler=Vote", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["optionId"] = optionId.ToString(),
        }));
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        var rejected = await member.GetStringAsync("/");
        Assert.Contains("already voted", rejected, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IHomePollRepository>();
        Assert.Equal(1, (await polls.GetCurrentAsync(null))!.TotalVotes);
    }

    private static async Task<Guid> PublishPollAsync(
        QueenZoneWebApplicationFactory host,
        string question,
        IReadOnlyList<string> options)
    {
        using var scope = host.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IHomePollRepository>();
        var id = await polls.CreateAsync(new AdminHomePollDraft(question, options), Guid.NewGuid());
        await polls.PublishAsync(id);
        return (await polls.GetCurrentAsync(null))!.Options[0].OptionId;
    }
}
