using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ForumAdminAuthorizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AllowlistedEmail = "admin@test.local";

    private readonly WebApplicationFactory<Program> factory;

    public ForumAdminAuthorizationTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, ExternalCookieTestHandler>(
                        MemberAuthenticationSchemes.ExternalCookie, _ => { });
            });
        });
    }

    [Fact]
    public async Task AllowlistedMember_CannotHideAuthor_AdminSchemeCan()
    {
        var authorId = Guid.NewGuid();
        var created = await CreateThreadAsync(authorId, "Hide auth author", "Hide auth subject");
        var topicPath = ForumRoutes.GetTopicCanonicalPath(created.TopicId, "Hide auth subject");

        var member = await CreateSignedInMemberClientAsync(AllowlistedEmail, "Allowlisted Member", "google-allowlisted-hide");
        var memberPage = await member.GetStringAsync(topicPath);
        Assert.DoesNotContain("Hide all posts by", memberPage);
        var forbidden = await member.PostAsync(
            $"/forum/post/{created.StarterPostId}/hide-author",
            Form(ExtractAntiforgeryToken(memberPage)));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var admin = CreateAdminClient();
        var adminPage = await admin.GetStringAsync(topicPath);
        Assert.Contains("Hide all posts by Hide auth author", adminPage);
        var confirmation = await admin.GetStringAsync($"/forum/post/{created.StarterPostId}/hide-author");
        Assert.Contains("Hide all posts and threads", confirmation);
        var hidden = await admin.PostAsync(
            $"/forum/post/{created.StarterPostId}/hide-author",
            Form(ExtractAntiforgeryToken(confirmation)));
        Assert.Equal(HttpStatusCode.Redirect, hidden.StatusCode);
    }

    [Fact]
    public async Task AnonymousHideAuthor_ChallengesAdminScheme_NotMemberLogin()
    {
        var created = await CreateThreadAsync(Guid.NewGuid(), "Anon hide author", "Anon hide subject");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync($"/forum/post/{created.StarterPostId}/hide-author");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("/account/login", response.Headers.Location?.OriginalString ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllowlistedMember_CannotCloseAnothersPoll_AdminAndAuthorCan()
    {
        var authorEmail = $"poll-author-{Guid.NewGuid():N}@example.com";
        var author = await CreateSignedInMemberClientAsync(
            authorEmail, "Poll Author", $"google-poll-author-{Guid.NewGuid():N}");
        var authorId = await GetMemberIdForEmailAsync(authorEmail);
        var subject = "Poll auth " + Guid.NewGuid().ToString("N");
        var (topicId, pollId) = await CreatePollThreadAsync(authorId, subject);
        var topicPath = ForumRoutes.GetTopicCanonicalPath(topicId, subject);

        var stranger = await CreateSignedInMemberClientAsync(AllowlistedEmail, "Allowlisted Member", "google-allowlisted-poll");
        var strangerPage = await stranger.GetStringAsync(topicPath);
        Assert.DoesNotContain("Close poll", strangerPage);
        Assert.DoesNotContain("Hide all posts by", strangerPage);
        var forbidden = await stranger.PostAsync($"/forum/poll/{pollId}/close", Form(ExtractAntiforgeryToken(strangerPage), topicPath));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var authorPage = await author.GetStringAsync(topicPath);
        Assert.Contains("Close poll", authorPage);

        var admin = CreateAdminClient();
        var adminPage = await admin.GetStringAsync(topicPath);
        Assert.Contains("Close poll", adminPage);
        var closed = await admin.PostAsync($"/forum/poll/{pollId}/close", Form(ExtractAntiforgeryToken(adminPage), topicPath));
        Assert.Equal(HttpStatusCode.Redirect, closed.StatusCode);

        using var scope = factory.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IForumPollRepository>();
        var results = await polls.GetPollWithResultsAsync(topicId, null);
        Assert.True(results!.IsClosed);
    }

    [Fact]
    public async Task PollAuthor_CanCloseOwnPoll_WithoutAdminScheme()
    {
        var email = $"closer-{Guid.NewGuid():N}@example.com";
        var author = await CreateSignedInMemberClientAsync(email, "Closer", $"google-closer-{Guid.NewGuid():N}");
        var authorId = await GetMemberIdForEmailAsync(email);
        var subject = "Author close " + Guid.NewGuid().ToString("N");
        var (topicId, pollId) = await CreatePollThreadAsync(authorId, subject);
        var topicPath = ForumRoutes.GetTopicCanonicalPath(topicId, subject);
        var page = await author.GetStringAsync(topicPath);

        var response = await author.PostAsync($"/forum/poll/{pollId}/close", Form(ExtractAntiforgeryToken(page), topicPath));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var polls = scope.ServiceProvider.GetRequiredService<IForumPollRepository>();
        Assert.True((await polls.GetPollWithResultsAsync(topicId, authorId))!.IsClosed);
    }

    private async Task<ForumThreadCreateResult> CreateThreadAsync(Guid authorId, string authorName, string subject)
    {
        using var scope = factory.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        return await write.CreateThreadAsync(new NewForumThread(
            1,
            authorId,
            authorName,
            subject,
            "<p>Body</p>",
            DateTimeOffset.UtcNow));
    }

    private async Task<(int TopicId, Guid PollId)> CreatePollThreadAsync(Guid authorId, string subject)
    {
        using var scope = factory.Services.CreateScope();
        var write = scope.ServiceProvider.GetRequiredService<IForumWriteRepository>();
        var polls = scope.ServiceProvider.GetRequiredService<IForumPollRepository>();
        var created = await write.CreateThreadAsync(new NewForumThread(
            1,
            authorId,
            "Poll Author",
            subject,
            "<p>With a poll</p>",
            DateTimeOffset.UtcNow,
            new NewForumPoll("Best album?", false, null, null, ["A", "B"], authorId)));
        var results = await polls.GetPollWithResultsAsync(created.TopicId, null);
        Assert.NotNull(results);
        return (created.TopicId, results!.PollId);
    }

    private HttpClient CreateAdminClient()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, AllowlistedEmail);
        return client;
    }

    private async Task<HttpClient> CreateSignedInMemberClientAsync(string email, string displayName, string subject)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, displayName);

        var callback = await client.GetAsync("/account/external-login-callback");
        Assert.True(
            callback.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
            $"Unexpected callback status code: {callback.StatusCode}");
        return client;
    }

    private async Task<Guid> GetMemberIdForEmailAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var members = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await members.FindByEmailAsync(email);
        Assert.NotNull(account);
        return account!.Id;
    }

    private static FormUrlEncodedContent Form(string token, string? returnUrl = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
        };
        if (returnUrl is not null)
        {
            fields["returnUrl"] = returnUrl;
        }

        return new FormUrlEncodedContent(fields);
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
