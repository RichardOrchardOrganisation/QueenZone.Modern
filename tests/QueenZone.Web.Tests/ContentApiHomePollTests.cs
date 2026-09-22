using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ContentApiHomePollTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Get_returns_json_null_when_no_current_poll()
    {
        using var isolated = IsolatedHomePolls();
        using var client = isolated.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ContentApiEndpoints.RootPath}/home-poll");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("null", (await response.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task Guest_can_read_results_and_cannot_vote()
    {
        using var isolated = IsolatedHomePolls();
        var optionId = await PublishPollAsync(isolated, "Best album?", ["Opera", "News"]);
        using var anonymous = isolated.CreateAnonymousClient(allowAutoRedirect: false);

        using var get = await anonymous.GetAsync($"{ContentApiEndpoints.RootPath}/home-poll");
        var poll = await get.Content.ReadFromJsonAsync<HomePollDto>(JsonOptions);
        Assert.NotNull(poll);
        Assert.Equal("Best album?", poll!.Question);
        Assert.Equal(2, poll.Options.Count);
        Assert.Equal(0, poll.TotalVotes);
        Assert.False(poll.ViewerHasVoted);
        Assert.Null(poll.SelectedOptionId);
        Assert.Contains(poll.Options, option => option.Text == "Opera" && option.Count == 0);

        using var vote = await anonymous.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/home-poll/votes",
            new { optionId });
        Assert.Equal(HttpStatusCode.Unauthorized, vote.StatusCode);
        Assert.Equal("application/problem+json", vote.Content.Headers.ContentType?.MediaType);

        using var cookieOnly = isolated.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");
        using var cookieVote = await cookieOnly.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/home-poll/votes",
            new { optionId });
        Assert.Equal(HttpStatusCode.Unauthorized, cookieVote.StatusCode);
    }

    [Fact]
    public async Task Member_votes_once_then_second_ballot_is_rejected()
    {
        using var isolated = IsolatedHomePolls();
        var optionId = await PublishPollAsync(isolated, "Q?", ["A", "B"]);
        var memberId = Guid.NewGuid();
        using var client = CreateBearerClient(isolated, memberId, "Poll Voter");

        using var before = await client.GetAsync($"{ContentApiEndpoints.RootPath}/home-poll");
        var open = await before.Content.ReadFromJsonAsync<HomePollDto>(JsonOptions);
        Assert.False(open!.ViewerHasVoted);

        using var vote = await client.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/home-poll/votes",
            new { optionId });
        Assert.Equal(HttpStatusCode.OK, vote.StatusCode);
        var voted = await vote.Content.ReadFromJsonAsync<HomePollDto>(JsonOptions);
        Assert.True(voted!.ViewerHasVoted);
        Assert.Equal(optionId, voted.SelectedOptionId);
        Assert.Equal(1, voted.TotalVotes);
        Assert.Equal(100, voted.Options[0].Percentage);

        using var second = await client.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/home-poll/votes",
            new { optionId });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status409Conflict, problem.GetProperty("status").GetInt32());
        Assert.Equal(ForumPollVoteException.AlreadyVoted, problem.GetProperty("code").GetString());

        using var after = await client.GetAsync($"{ContentApiEndpoints.RootPath}/home-poll");
        var still = await after.Content.ReadFromJsonAsync<HomePollDto>(JsonOptions);
        Assert.Equal(1, still!.TotalVotes);
    }

    [Fact]
    public async Task Closed_poll_rejects_votes_and_unpublished_is_hidden()
    {
        using var isolated = IsolatedHomePolls();
        var optionId = await PublishPollAsync(isolated, "Close me?", ["Yes", "No"]);
        using var scope = isolated.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IHomePollRepository>();
        var current = await polls.GetCurrentAsync(null);
        await polls.CloseAsync(current!.PollId);

        using var voter = CreateBearerClient(isolated, Guid.NewGuid(), "Late Voter");
        using var late = await voter.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/home-poll/votes",
            new { optionId });
        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
        var closedProblem = await late.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ForumPollVoteException.Closed, closedProblem.GetProperty("code").GetString());

        using var closedGet = await voter.GetAsync($"{ContentApiEndpoints.RootPath}/home-poll");
        var closed = await closedGet.Content.ReadFromJsonAsync<HomePollDto>(JsonOptions);
        Assert.True(closed!.IsClosed);

        await polls.HideAsync(current.PollId);
        using var hidden = await voter.GetAsync($"{ContentApiEndpoints.RootPath}/home-poll");
        Assert.Equal("null", (await hidden.Content.ReadAsStringAsync()).Trim());
    }

    [Fact]
    public async Task Website_index_and_api_expose_the_same_current_poll()
    {
        using var isolated = IsolatedHomePolls();
        await PublishPollAsync(isolated, "Same contract?", ["Web", "Mobile"]);
        using var client = isolated.CreateAnonymousClient();

        using var api = await client.GetAsync($"{ContentApiEndpoints.RootPath}/home-poll");
        var poll = await api.Content.ReadFromJsonAsync<HomePollDto>(JsonOptions);
        var html = await client.GetStringAsync("/");

        Assert.Contains(poll!.Question, html, StringComparison.Ordinal);
        Assert.Contains("Web", html, StringComparison.Ordinal);
        Assert.Contains("Mobile", html, StringComparison.Ordinal);
        Assert.Contains("id=\"home-poll\"", html, StringComparison.Ordinal);
        Assert.Contains("Sign in to vote", html, StringComparison.Ordinal);
        Assert.Contains("0 · 0%", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suspended_member_cannot_vote()
    {
        using var isolated = IsolatedHomePolls();
        var optionId = await PublishPollAsync(isolated, "Q?", ["A", "B"]);
        var memberId = Guid.NewGuid();
        using (var scope = isolated.Services.CreateScope())
        {
            var members = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
            await members.CreateAsync(new QueenZone.Data.Entities.MemberAccount
            {
                Id = memberId,
                Email = "suspended@example.test",
                DisplayName = "Suspended",
                CreatedAt = DateTime.UtcNow,
                IsSuspended = true,
            });
        }

        using var client = CreateBearerClient(isolated, memberId, "Suspended");
        using var vote = await client.PostAsJsonAsync(
            $"{ContentApiEndpoints.RootPath}/home-poll/votes",
            new { optionId });
        Assert.Equal(HttpStatusCode.Unauthorized, vote.StatusCode);
        Assert.Equal("application/problem+json", vote.Content.Headers.ContentType?.MediaType);
    }

    private static QueenZoneWebApplicationFactory IsolatedHomePolls()
    {
        var store = new SharedHomePollStore();
        return QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.RemoveAll<SharedHomePollStore>();
            services.RemoveAll<IHomePollRepository>();
            services.AddSingleton(store);
            services.AddSingleton<IHomePollRepository>(_ => new InMemoryHomePollRepository(store));
        });
    }

    private static async Task<Guid> PublishPollAsync(
        QueenZoneWebApplicationFactory factory,
        string question,
        IReadOnlyList<string> options)
    {
        using var scope = factory.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IHomePollRepository>();
        var id = await polls.CreateAsync(new AdminHomePollDraft(question, options), Guid.NewGuid());
        await polls.PublishAsync(id);
        var current = await polls.GetCurrentAsync(null);
        return current!.Options[0].OptionId;
    }

    private static HttpClient CreateBearerClient(
        QueenZoneWebApplicationFactory factory,
        Guid memberId,
        string displayName)
    {
        MemberBearerAccounts.Ensure(factory.Services, memberId, $"{memberId:N}@example.test", displayName);
        using var scope = factory.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var token = issuer.IssueAccessToken(memberId, $"{memberId:N}@example.test", displayName);
        var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
