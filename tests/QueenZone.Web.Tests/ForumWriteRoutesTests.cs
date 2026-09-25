using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ForumWriteRoutesTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ForumWriteRoutesTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task NewThreadGet_RedirectsAnonymousMemberToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/forum/c/the-music/new-thread");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task NewThreadGet_HidesEmptyValidationSummaryWithoutHidingErrors()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());

        var page = await client.GetStringAsync("/forum/c/the-music/new-thread");
        var css = await client.GetStringAsync("/css/site.css");

        Assert.Contains("validation-summary-valid", page);
        Assert.Contains(".validation-summary-valid", css);
        Assert.Matches(@"\.validation-summary-valid\s*\{\s*display:\s*none;\s*\}", css);
    }

    [Fact]
    public async Task NewThreadGet_UsesSharedFormFieldClasses()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());

        var page = await client.GetStringAsync("/forum/c/the-music/new-thread");
        var css = await client.GetStringAsync("/css/site.css");

        Assert.DoesNotContain("qz-form__field", page);
        Assert.DoesNotContain(".qz-form__field", css);
        Assert.Contains("qz-field qz-field--spaced", page);
        Assert.Contains("class=\"qz-label\"", page);
        Assert.Contains("class=\"qz-input\"", page);
        Assert.Contains("qz-error qz-error--text", page);
    }

    [Fact]
    public async Task NewThreadGet_LabelsEachPollOptionAndTheOptionsGroup()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());

        var page = await client.GetStringAsync("/forum/c/the-music/new-thread");

        Assert.Contains("id=\"poll-options-label\"", page);
        Assert.Contains("role=\"group\"", page);
        Assert.Contains("aria-labelledby=\"poll-options-label\"", page);
        Assert.Contains("id=\"Poll_Option_0\"", page);
        Assert.Contains("aria-label=\"Option 1\"", page);
        Assert.Contains("id=\"Poll_Option_1\"", page);
        Assert.Contains("aria-label=\"Option 2\"", page);
        Assert.DoesNotContain("<label class=\"qz-label\">Options</label>", page);
    }

    [Fact]
    public async Task ValidNewThreadPost_CreatesThreadAndRedirectsToExistingTopicRoute()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());
        var form = await client.GetStringAsync("/forum/c/the-music/new-thread");
        var token = ExtractAntiforgeryToken(form);

        var response = await client.PostAsync("/forum/c/the-music/new-thread", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Subject"] = "Fresh forum news",
            ["Body"] = "<p>Hello <strong>fans</strong><script>alert(1)</script></p>",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/forum/topic/", response.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var redirected = await client.GetStringAsync(response.Headers.Location);
        Assert.DoesNotContain("qz-forum-poll", redirected);
    }

    [Fact]
    public async Task TopicPage_AuthorNameLinksToPublicMemberProfile_WhenAuthorMemberIdPresent()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAccountAsync(memberId, "Profile Linked Fan");
        var client = CreateMemberClient(factory, memberId, "Profile Linked Fan");
        var form = await client.GetStringAsync("/forum/c/the-music/new-thread");
        var token = ExtractAntiforgeryToken(form);

        var response = await client.PostAsync("/forum/c/the-music/new-thread", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Subject"] = "Profile link check thread",
            ["Body"] = "<p>Author name should link to my public profile.</p>",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var topicHtml = await client.GetStringAsync(response.Headers.Location!);

        Assert.Contains($"href=\"/members/{memberId}\"", topicHtml);
        Assert.Contains("Profile Linked Fan", topicHtml);
        Assert.Contains("qz-forum-post__author-link", topicHtml);

        // Legacy seed authors (no AuthorMemberId) stay plain text, not profile links.
        var archiveBody = await factory.CreateClient().GetStringAsync("/forum/topic/1001/forum-guidelines");
        Assert.Contains("<strong class=\"qz-forum-post__author\">Richard Orchard</strong>", archiveBody);
        Assert.DoesNotContain("href=\"/members/", archiveBody);
    }

    [Fact]
    public async Task ValidNewThreadPost_WithPoll_CreatesThreadAndRedirectedTopicRendersPoll()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());
        var form = await client.GetStringAsync("/forum/c/the-music/new-thread");
        var token = ExtractAntiforgeryToken(form);

        var response = await client.PostAsync("/forum/c/the-music/new-thread", new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("Subject", "Fresh poll thread"),
            new KeyValuePair<string, string>("Body", "<p>Vote on this</p>"),
            new KeyValuePair<string, string>("Poll.Enabled", "true"),
            new KeyValuePair<string, string>("Poll.Enabled", "false"),
            new KeyValuePair<string, string>("Poll.Question", "Which Queen era?"),
            new KeyValuePair<string, string>("Poll.Options[0]", "Seventies"),
            new KeyValuePair<string, string>("Poll.Options[1]", "Eighties"),
            new KeyValuePair<string, string>("Poll.Options[2]", "Nineties"),
            new KeyValuePair<string, string>("Poll.Options[3]", "Now"),
        ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/forum/topic/", response.Headers.Location!.OriginalString, StringComparison.Ordinal);

        var redirected = await client.GetStringAsync(response.Headers.Location);
        Assert.Contains("Which Queen era?", redirected);
        Assert.Contains("Seventies", redirected);
        Assert.Contains("Eighties", redirected);
        Assert.Contains("Nineties", redirected);
        Assert.Contains("Now", redirected);
        Assert.Contains("qz-forum-poll", redirected);
    }

    [Fact]
    public async Task ExistingTopicPost_CreatesReplyAndRedirectsToPostAnchor()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());
        var page = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");
        var token = ExtractAntiforgeryToken(page);

        var response = await client.PostAsync("/forum/topic/1002/ranking-every-studio-album", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "<p>New reply<script>alert(1)</script></p>",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/forum/topic/1002/ranking-every-studio-album", response.Headers.Location!.OriginalString);
        Assert.Contains("#post-", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ExistingTopicPost_UsesPersistedMemberDisplayName()
    {
        var memberId = Guid.NewGuid();
        await SeedMemberAccountAsync(memberId, "Richard Orchard");
        var client = CreateMemberClient(factory, memberId, "Member");
        var page = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");
        var token = ExtractAntiforgeryToken(page);

        var response = await client.PostAsync("/forum/topic/1002/ranking-every-studio-album", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "Display name regression reply",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var redirected = await client.GetStringAsync(response.Headers.Location);
        Assert.Contains("Richard Orchard", redirected);
        Assert.Contains("Display name regression reply", redirected);
    }

    [Fact]
    public async Task ExistingTopicPost_RedirectsAnonymousMemberToLogin()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());
        var page = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");
        var token = ExtractAntiforgeryToken(page);
        client.DefaultRequestHeaders.Remove(TestMemberAuthHandler.MemberIdHeader);
        client.DefaultRequestHeaders.Remove(TestMemberAuthHandler.DisplayNameHeader);

        var response = await client.PostAsync("/forum/topic/1002/ranking-every-studio-album", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "<p>New reply</p>",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task ExistingTopicPost_ReturnsNotFoundForMissingTopic()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());
        var page = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");
        var token = ExtractAntiforgeryToken(page);

        var response = await client.PostAsync("/forum/topic/9999/missing-topic", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "<p>New reply</p>",
        }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExistingTopicPost_RerendersWhenBodySanitizesToEmpty()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());
        var page = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");
        var token = ExtractAntiforgeryToken(page);

        var response = await client.PostAsync("/forum/topic/1002/ranking-every-studio-album", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "<script>alert(1)</script>",
        }));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("validation-summary-errors", body);
        Assert.Contains("Body is required.", body);
    }

    [Fact]
    public async Task ExistingTopicPost_ReturnsTooManyRequestsAfterRateLimit()
    {
        var memberId = Guid.NewGuid();
        var client = CreateMemberClient(factory, memberId);

        HttpResponseMessage? response = null;
        for (var i = 0; i < ForumPostRateLimiter.MaxPostsPerMinute + 1; i++)
        {
            var page = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");
            var token = ExtractAntiforgeryToken(page);
            response = await client.PostAsync("/forum/topic/1002/ranking-every-studio-album", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["Body"] = $"<p>Reply {i}</p>",
            }));
        }

        Assert.NotNull(response);
        Assert.Equal((HttpStatusCode)429, response!.StatusCode);
    }

    [Fact]
    public async Task NewThreadButton_IsVisibleOnCategoryPageForAuthenticatedMember()
    {
        var client = CreateMemberClient(factory, Guid.NewGuid());

        var body = await client.GetStringAsync("/forum/1/the-music");

        Assert.Contains("href=\"/forum/c/the-music/new-thread\"", body);
        Assert.Contains(">New thread<", body);
    }

    [Fact]
    public async Task LockedTopicPost_ReturnsForbidden()
    {
        var lockedFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IForumWriteRepository>();
                services.AddSingleton<IForumWriteRepository>(new LockedForumWriteRepository());
            });
        });
        var client = CreateMemberClient(lockedFactory, Guid.NewGuid());
        var page = await client.GetStringAsync("/forum/topic/1002/ranking-every-studio-album");
        var token = ExtractAntiforgeryToken(page);

        var response = await client.PostAsync("/forum/topic/1002/ranking-every-studio-album", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Body"] = "<p>Nope</p>",
        }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task SeedMemberAccountAsync(Guid memberId, string displayName)
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        await repository.CreateAsync(new MemberAccount
        {
            Id = memberId,
            Email = $"{memberId:N}@example.test",
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static HttpClient CreateMemberClient(
        WebApplicationFactory<Program> sourceFactory,
        Guid memberId,
        string displayName = "Forum Fan")
    {
        var client = sourceFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.MemberIdHeader, memberId.ToString());
        client.DefaultRequestHeaders.Add(TestMemberAuthHandler.DisplayNameHeader, displayName);
        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var input = Regex.Match(
            html,
            """<input[^>]*name="__RequestVerificationToken"[^>]*>""",
            RegexOptions.IgnoreCase);
        Assert.True(input.Success, "Antiforgery token input was not found in the form.");

        var value = Regex.Match(input.Value, "value=\"(?<token>[^\"]+)\"", RegexOptions.IgnoreCase);
        Assert.True(value.Success, "Antiforgery token value was not found in the form.");
        return value.Groups["token"].Value;
    }

    private sealed class LockedForumWriteRepository : IForumWriteRepository
    {
        public Task<ForumThreadCreateResult> CreateThreadAsync(NewForumThread thread, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumThreadCreateResult(200_001, 2_000_001));

        public Task<int> CreatePostAsync(NewForumPost post, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Locked.");

        public Task<ForumEditablePost?> GetPostAsync(int postId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumEditablePost?>(null);

        public Task<ForumPostUpdateResult> UpdatePostAsync(
            int postId,
            Guid editorMemberId,
            string sanitisedBody,
            bool isAdmin,
            int editWindowMinutes,
            DateTimeOffset? expectedUpdatedAt = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ForumPostUpdateResult(ForumPostUpdateStatus.Forbidden));

        public Task<ForumWriteThread?> GetThreadAsync(int topicId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ForumWriteThread?>(new ForumWriteThread(
                topicId,
                1,
                "Ranking every studio album",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                1,
                IsLocked: true));

        public Task<int> CountPostsByMemberSinceAsync(Guid memberId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task<int> CountApprovedPostsByMemberAsync(Guid memberId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);

        public Task HideAuthorForumContentAsync(Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task UnhideAuthorForumContentAsync(Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> EnsureCategoryAsync(
            string slug,
            string name,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(1);
    }
}
