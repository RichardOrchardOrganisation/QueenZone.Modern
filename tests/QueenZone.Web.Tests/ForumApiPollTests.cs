using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumApiPollTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly QueenZoneWebApplicationFactory factory;

    public ForumApiPollTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Poll_get_requires_no_auth_and_matches_website_results_for_anonymous()
    {
        var authorId = Guid.NewGuid();
        var (topicId, pollId, optionId) = await CreateThreadWithPollAsync(authorId);
        using var client = factory.CreateAnonymousClient();

        using var response = await client.GetAsync($"{ForumApiEndpoints.RootPath}/topics/{topicId}/poll");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var poll = await response.Content.ReadFromJsonAsync<ForumPollDto>(JsonOptions);
        Assert.NotNull(poll);
        Assert.Equal(pollId, poll!.PollId);
        Assert.Equal(topicId, poll.TopicId);
        Assert.Equal("Best Queen album?", poll.Question);
        Assert.False(poll.IsClosed);
        Assert.False(poll.CanViewerVote);
        Assert.False(poll.ViewerHasVoted);
        Assert.False(poll.CanViewerClose);
        Assert.Equal(0, poll.DistinctVoters);
        Assert.Equal(2, poll.Options.Count);
        Assert.Equal(optionId, poll.Options[0].OptionId);
        Assert.Equal("Night at the Opera", poll.Options[0].OptionText);
        Assert.Equal(0, poll.Options[0].VoteCount);
        Assert.Equal(0, poll.Options[0].Percentage);

        var html = await client.GetStringAsync($"/forum/topic/{topicId}/poll-topic");
        Assert.Contains("Best Queen album?", html, StringComparison.Ordinal);
        Assert.Contains("Night at the Opera", html, StringComparison.Ordinal);
        Assert.Contains("Sign in to vote", html, StringComparison.Ordinal);
        Assert.Contains("0 · 0%", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Poll_get_lets_signed_in_member_vote_once_then_rejects_second_ballot()
    {
        var authorId = Guid.NewGuid();
        var (topicId, _, optionId) = await CreateThreadWithPollAsync(authorId);
        var voterId = Guid.NewGuid();
        using var client = CreateBearerClient(voterId, "Poll Voter");

        using var beforeVote = await client.GetAsync($"{ForumApiEndpoints.RootPath}/topics/{topicId}/poll");
        var open = await beforeVote.Content.ReadFromJsonAsync<ForumPollDto>(JsonOptions);
        Assert.NotNull(open);
        Assert.True(open!.CanViewerVote);
        Assert.False(open.ViewerHasVoted);

        using var vote = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{topicId}/poll/vote",
            new { optionId });
        Assert.Equal(HttpStatusCode.OK, vote.StatusCode);
        var voted = await vote.Content.ReadFromJsonAsync<ForumPollDto>(JsonOptions);
        Assert.NotNull(voted);
        Assert.False(voted!.CanViewerVote);
        Assert.True(voted.ViewerHasVoted);
        Assert.Equal(1, voted.TotalVotes);
        Assert.Equal(1, voted.DistinctVoters);
        Assert.Equal(100, voted.Options[0].Percentage);
        Assert.True(voted.Options[0].SelectedByViewer);
        Assert.Contains(voted.Options, option => option.OptionId == optionId && option.SelectedByViewer);

        using var second = await client.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{topicId}/poll/vote",
            new { optionId });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(StatusCodes.Status409Conflict, problem.GetProperty("status").GetInt32());
        Assert.Equal(ForumPollVoteException.AlreadyVoted, problem.GetProperty("code").GetString());
        Assert.Contains("already voted", problem.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Poll_vote_and_close_require_bearer_token()
    {
        var authorId = Guid.NewGuid();
        var (topicId, _, optionId) = await CreateThreadWithPollAsync(authorId);
        using var anonymous = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var cookieOnly = factory.CreateAnonymousClient(allowAutoRedirect: false);
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, Guid.NewGuid().ToString());
        cookieOnly.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, "Cookie Fan");

        foreach (var client in new[] { anonymous, cookieOnly })
        {
            using var vote = await client.PostAsJsonAsync(
                $"{ForumApiEndpoints.RootPath}/topics/{topicId}/poll/vote",
                new { optionId });
            Assert.Equal(HttpStatusCode.Unauthorized, vote.StatusCode);
            Assert.Equal("application/problem+json", vote.Content.Headers.ContentType?.MediaType);

            using var close = await client.PostAsync(
                $"{ForumApiEndpoints.RootPath}/topics/{topicId}/poll/close",
                null);
            Assert.Equal(HttpStatusCode.Unauthorized, close.StatusCode);
            Assert.Equal("application/problem+json", close.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Poll_vote_rejects_empty_and_closed_ballots()
    {
        var authorId = Guid.NewGuid();
        var (topicId, _, _) = await CreateThreadWithPollAsync(authorId);
        using var author = CreateBearerClient(authorId, "Poll Author");

        using var empty = await author.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{topicId}/poll/vote",
            new { });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var emptyProblem = await empty.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ForumPollVoteException.InvalidOptions, emptyProblem.GetProperty("code").GetString());

        using var closed = await author.PostAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{topicId}/poll/close",
            null);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var closedPoll = await closed.Content.ReadFromJsonAsync<ForumPollDto>(JsonOptions);
        Assert.NotNull(closedPoll);
        Assert.True(closedPoll!.IsClosed);
        Assert.False(closedPoll.CanViewerVote);
        Assert.False(closedPoll.CanViewerClose);

        using var voter = CreateBearerClient(Guid.NewGuid(), "Late Voter");
        using var lateVote = await voter.PostAsJsonAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{topicId}/poll/vote",
            new { optionId = closedPoll.Options[0].OptionId });
        Assert.Equal(HttpStatusCode.BadRequest, lateVote.StatusCode);
        var lateProblem = await lateVote.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ForumPollVoteException.Closed, lateProblem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Poll_close_is_forbidden_for_non_author()
    {
        var authorId = Guid.NewGuid();
        var (topicId, _, _) = await CreateThreadWithPollAsync(authorId);
        using var other = CreateBearerClient(Guid.NewGuid(), "Other Member");

        using var response = await other.PostAsync(
            $"{ForumApiEndpoints.RootPath}/topics/{topicId}/poll/close",
            null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(ForumPollVoteException.Forbidden, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Poll_get_returns_problem_details_for_missing_topic_and_missing_poll()
    {
        using var client = factory.CreateAnonymousClient();

        using var missingTopic = await client.GetAsync($"{ForumApiEndpoints.RootPath}/topics/9999/poll");
        Assert.Equal(HttpStatusCode.NotFound, missingTopic.StatusCode);
        var missingTopicProblem = await missingTopic.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("9999", missingTopicProblem.GetProperty("detail").GetString(), StringComparison.Ordinal);

        using var noPoll = await client.GetAsync($"{ForumApiEndpoints.RootPath}/topics/1002/poll");
        Assert.Equal(HttpStatusCode.NotFound, noPoll.StatusCode);
        var noPollProblem = await noPoll.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("No poll", noPollProblem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    private async Task<(int TopicId, Guid PollId, Guid OptionId)> CreateThreadWithPollAsync(Guid authorId)
    {
        using var scope = factory.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        var polls = scope.ServiceProvider.GetRequiredService<IForumPollRepository>();

        var created = await write.CreateThreadAsync(new NewForumThread(
            CategoryId: 1,
            AuthorMemberId: authorId,
            AuthorDisplayName: "Poll Author",
            Subject: "Poll topic",
            Body: "<p>With a poll</p>",
            CreatedAt: DateTimeOffset.UtcNow,
            Poll: new NewForumPoll(
                "Best Queen album?",
                false,
                null,
                null,
                ["Night at the Opera", "Sheer Heart Attack"],
                authorId)));

        var results = await polls.GetPollWithResultsAsync(created.TopicId, null);
        Assert.NotNull(results);
        return (created.TopicId, results!.PollId, results.Options[0].OptionId);
    }

    private HttpClient CreateBearerClient(Guid memberId, string displayName = "Forum Fan")
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
