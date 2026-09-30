using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumPollEndpointsTests : IClassFixture<ExternalCookieWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ForumPollEndpointsTests(ExternalCookieWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task VotePost_CreatesVoteAndRedirects()
    {
        var authorId = Guid.NewGuid();
        var (topicId, pollId, optionId) = await CreateThreadWithPollAsync(authorId);
        var client = await CreateSignedInMemberClientAsync();

        var topicPage = await client.GetStringAsync($"/forum/topic/{topicId}/poll-topic");
        var token = ExtractAntiforgeryToken(topicPage);

        using var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent($"/forum/topic/{topicId}/poll-topic"), "returnUrl" },
            { new StringContent(optionId.ToString()), "optionId" },
        };

        var response = await client.PostAsync($"/forum/poll/{pollId}/vote", content);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("#poll", response.Headers.Location!.OriginalString);

        using var scope = factory.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IForumPollRepository>();
        // Signed-in member id is created by external login callback — look up by vote counts.
        var results = await polls.GetPollWithResultsAsync(topicId, null);
        Assert.NotNull(results);
        Assert.Equal(1, results!.TotalVotes);
        Assert.Equal(1, results.DistinctVoters);
    }

    [Fact]
    public async Task VotePost_SecondVote_ReturnsConflict()
    {
        var authorId = Guid.NewGuid();
        var (topicId, pollId, optionId) = await CreateThreadWithPollAsync(authorId);
        var client = await CreateSignedInMemberClientAsync();

        var topicPage = await client.GetStringAsync($"/forum/topic/{topicId}/poll-topic");
        var token = ExtractAntiforgeryToken(topicPage);

        using var first = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent($"/forum/topic/{topicId}/poll-topic"), "returnUrl" },
            { new StringContent(optionId.ToString()), "optionId" },
        };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync($"/forum/poll/{pollId}/vote", first)).StatusCode);

        topicPage = await client.GetStringAsync($"/forum/topic/{topicId}/poll-topic");
        token = ExtractAntiforgeryToken(topicPage);
        using var second = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent($"/forum/topic/{topicId}/poll-topic"), "returnUrl" },
            { new StringContent(optionId.ToString()), "optionId" },
        };
        var secondResponse = await client.PostAsync($"/forum/poll/{pollId}/vote", second);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task TopicPage_ShowsPollQuestion_WhenPresent()
    {
        var authorId = Guid.NewGuid();
        var (topicId, _, _) = await CreateThreadWithPollAsync(authorId);
        var client = factory.CreateClient();

        var body = await client.GetStringAsync($"/forum/topic/{topicId}/poll-topic");

        Assert.Contains("Best Queen album?", body);
        Assert.Contains("Sign in to vote", body);
        Assert.Contains("Night at the Opera", body);
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

    private async Task<HttpClient> CreateSignedInMemberClientAsync()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, $"google-poll-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, $"poller-{Guid.NewGuid():N}@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Poll Voter");

        var callbackResponse = await client.GetAsync("/account/external-login-callback");
        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
        Assert.DoesNotContain("/account/login", callbackResponse.Headers.Location!.OriginalString);
        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var input = Regex.Match(
            html,
            """<input[^>]*name="__RequestVerificationToken"[^>]*>""",
            RegexOptions.IgnoreCase);
        Assert.True(input.Success, "Antiforgery token input was not found.");
        var value = Regex.Match(input.Value, "value=\"(?<token>[^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(value.Success, "Antiforgery token value was not found.");
        return value.Groups["token"].Value;
    }
}
