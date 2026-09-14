using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class AdminMembersRoutesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AdminEmail = "admin@test.local";
    private readonly WebApplicationFactory<Program> factory;

    public AdminMembersRoutesTests(WebApplicationFactory<Program> factory)
    {
        this.factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureTestServices(services =>
            {
                services
                    .AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, ExternalCookieTestHandler>(
                        MemberAuthenticationSchemes.ExternalCookie, _ => { });
            });
        });
    }

    [Fact]
    public async Task Get_AdminMembers_RequiresAdminAuthentication()
    {
        var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/admin/members")).StatusCode);

        var stranger = CreateAdminClient("stranger@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync("/admin/members")).StatusCode);

        var admin = CreateAdminClient(AdminEmail);
        var body = await admin.GetStringAsync("/admin/members");
        Assert.Contains("Members", body);
    }

    [Fact]
    public async Task Get_AdminMembers_SearchFiltersByDisplayNameOrEmail()
    {
        await CreateSignedInMemberClientAsync("spammer@example.com", "Spam Bot", "google-spam-search");
        await CreateSignedInMemberClientAsync("regular@example.com", "Regular Fan", "google-regular-search");

        var admin = CreateAdminClient(AdminEmail);
        var body = await admin.GetStringAsync("/admin/members?query=Spam");

        Assert.Contains("Spam Bot", body);
        Assert.DoesNotContain("Regular Fan", body);
    }

    [Fact]
    public async Task Suspend_RequiresReason()
    {
        var memberClient = await CreateSignedInMemberClientAsync(
            "no-reason@example.com", "No Reason Fan", "google-no-reason");
        var memberId = await GetMemberIdForEmailAsync("no-reason@example.com");

        var admin = CreateAdminClient(AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/members/{memberId}");
        var response = await admin.PostAsync(
            $"/admin/members/{memberId}/Suspend",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var members = factory.Services.GetRequiredService<IMemberAccountRepository>();
        var account = await members.FindByIdAsync(memberId);
        Assert.False(account!.IsSuspended);

        _ = memberClient;
    }

    [Fact]
    public async Task Suspend_BlocksFutureSignIn_AndEndsExistingSession_AndReinstateRestoresAccess()
    {
        const string email = "board-spammer@example.com";
        var memberClient = await CreateSignedInMemberClientAsync(email, "Board Spammer", "google-board-spammer");
        var memberId = await GetMemberIdForEmailAsync(email);

        // The member's existing session works before suspension.
        var beforeSuspension = await memberClient.GetAsync("/account/settings");
        Assert.Equal(HttpStatusCode.OK, beforeSuspension.StatusCode);

        var admin = CreateAdminClient(AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/members/{memberId}");
        var suspendResponse = await admin.PostAsync(
            $"/admin/members/{memberId}/Suspend",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
                ["Reason"] = "Posting spam links across the forum",
            }));
        Assert.Equal(HttpStatusCode.Redirect, suspendResponse.StatusCode);

        var members = factory.Services.GetRequiredService<IMemberAccountRepository>();
        var suspended = await members.FindByIdAsync(memberId);
        Assert.True(suspended!.IsSuspended);
        Assert.Equal("Posting spam links across the forum", suspended.SuspendedReason);
        Assert.Equal(AdminEmail, suspended.SuspendedByAdminEmail);

        // The member's existing cookie session is rejected on their very next request.
        var afterSuspensionOptions = new HttpRequestMessage(HttpMethod.Get, "/account/settings");
        var afterSuspension = await memberClient.SendAsync(afterSuspensionOptions);
        Assert.NotEqual(HttpStatusCode.OK, afterSuspension.StatusCode);

        // A fresh OAuth sign-in attempt is also rejected.
        var retryClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        retryClient.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        retryClient.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-board-spammer");
        retryClient.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        retryClient.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Board Spammer");
        var retryCallback = await retryClient.GetAsync("/account/external-login-callback");
        Assert.Equal(HttpStatusCode.Redirect, retryCallback.StatusCode);
        Assert.Contains("suspended=1", retryCallback.Headers.Location!.OriginalString);

        // Reinstating restores sign-in access.
        var reinstateDetail = await admin.GetStringAsync($"/admin/members/{memberId}");
        var reinstateResponse = await admin.PostAsync(
            $"/admin/members/{memberId}/Reinstate",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(reinstateDetail),
            }));
        Assert.Equal(HttpStatusCode.Redirect, reinstateResponse.StatusCode);

        var reinstated = await members.FindByIdAsync(memberId);
        Assert.False(reinstated!.IsSuspended);

        var freshSignIn = await CreateSignedInMemberClientAsync(email, "Board Spammer", "google-board-spammer");
        var settingsAfterReinstate = await freshSignIn.GetAsync("/account/settings");
        Assert.Equal(HttpStatusCode.OK, settingsAfterReinstate.StatusCode);
    }

    [Fact]
    public async Task Suspend_HidesMembersForumPosts_AndReinstateRestoresThem()
    {
        const string email = "post-spammer@example.com";
        await CreateSignedInMemberClientAsync(email, "Post Spammer", "google-post-spammer");
        var memberId = await GetMemberIdForEmailAsync(email);

        var forumWriteRepository = factory.Services.GetRequiredService<IForumWriteRepository>();
        await forumWriteRepository.CreateThreadAsync(new NewForumThread(
            1,
            memberId,
            "Post Spammer",
            "Buy cheap watches here",
            "<p>Click this link for cheap watches</p>",
            DateTimeOffset.UtcNow));

        var profileClient = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var beforeSuspend = await profileClient.GetStringAsync($"/members/{memberId}");
        Assert.Contains("Buy cheap watches here", beforeSuspend);
        var topicsBeforeSuspend = await profileClient.GetStringAsync("/api/v1/forum/categories/1/topics");
        Assert.Contains("Buy cheap watches here", topicsBeforeSuspend);

        var admin = CreateAdminClient(AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/members/{memberId}");
        var suspendResponse = await admin.PostAsync(
            $"/admin/members/{memberId}/Suspend",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
                ["Reason"] = "Posting spam links",
            }));
        Assert.Equal(HttpStatusCode.Redirect, suspendResponse.StatusCode);

        var afterSuspend = await profileClient.GetStringAsync($"/members/{memberId}");
        Assert.DoesNotContain("Buy cheap watches here", afterSuspend);
        var topicsAfterSuspend = await profileClient.GetStringAsync("/api/v1/forum/categories/1/topics");
        Assert.DoesNotContain("Buy cheap watches here", topicsAfterSuspend);

        var reinstateDetail = await admin.GetStringAsync($"/admin/members/{memberId}");
        var reinstateResponse = await admin.PostAsync(
            $"/admin/members/{memberId}/Reinstate",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(reinstateDetail),
            }));
        Assert.Equal(HttpStatusCode.Redirect, reinstateResponse.StatusCode);

        var afterReinstate = await profileClient.GetStringAsync($"/members/{memberId}");
        Assert.Contains("Buy cheap watches here", afterReinstate);
        var topicsAfterReinstate = await profileClient.GetStringAsync("/api/v1/forum/categories/1/topics");
        Assert.Contains("Buy cheap watches here", topicsAfterReinstate);
    }

    [Fact]
    public async Task Suspend_SecondPostIsIdempotent()
    {
        const string email = "idempotent-spammer@example.com";
        await CreateSignedInMemberClientAsync(email, "Idempotent Spammer", "google-idempotent-spammer");
        var memberId = await GetMemberIdForEmailAsync(email);
        var forum = factory.Services.GetRequiredService<IForumWriteRepository>();
        await forum.CreateThreadAsync(new NewForumThread(
            1, memberId, "Idempotent Spammer", "Idempotent spam", "<p>spam</p>", DateTimeOffset.UtcNow));

        var admin = CreateAdminClient(AdminEmail);
        var firstDetail = await admin.GetStringAsync($"/admin/members/{memberId}");
        var first = await admin.PostAsync(
            $"/admin/members/{memberId}/Suspend",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(firstDetail),
                ["Reason"] = "Repeat suspend",
            }));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal($"/admin/members/{memberId}", first.Headers.Location!.OriginalString);

        var secondDetail = await admin.GetStringAsync($"/admin/members/{memberId}");
        Assert.Contains(AdminMemberSuspendService.SuccessMessage, secondDetail);
        var second = await admin.PostAsync(
            $"/admin/members/{memberId}/Suspend",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(secondDetail),
                ["Reason"] = "Repeat suspend",
            }));
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal($"/admin/members/{memberId}", second.Headers.Location!.OriginalString);
        Assert.DoesNotContain("/error/", second.Headers.Location.OriginalString, StringComparison.Ordinal);

        var after = await admin.GetStringAsync($"/admin/members/{memberId}");
        Assert.Contains(AdminMemberSuspendService.SuccessMessage, after);
        Assert.True((await factory.Services.GetRequiredService<IMemberAccountRepository>().FindByIdAsync(memberId))!.IsSuspended);
        Assert.DoesNotContain("Idempotent spam", await factory.CreateClient().GetStringAsync("/api/v1/forum/categories/1/topics"));
    }

    [Fact]
    public async Task Suspend_WhenHideTimesOut_RedirectsToMemberPageWithError_Not404()
    {
        const string email = "timeout-spammer@example.com";
        var timeoutFactory = CreateTimeoutHideFactory();
        var memberId = await CreateMemberAsync(timeoutFactory, email, "Timeout Spammer", "google-timeout-spammer");
        var admin = CreateAdminClient(timeoutFactory, AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/members/{memberId}");
        var response = await admin.PostAsync(
            $"/admin/members/{memberId}/Suspend",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
                ["Reason"] = "Spam",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/admin/members/{memberId}", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("/error/", response.Headers.Location.OriginalString, StringComparison.Ordinal);

        var page = await admin.GetStringAsync($"/admin/members/{memberId}");
        Assert.Contains(AdminMemberSuspendService.HideTimeoutMessage, page);
        Assert.DoesNotContain("Page Not Found", page);
        Assert.False((await timeoutFactory.Services.GetRequiredService<IMemberAccountRepository>().FindByIdAsync(memberId))!.IsSuspended);
    }

    [Fact]
    public async Task Suspend_WhenRevokeFails_RedirectsToMemberPageWithRetryRevokeError()
    {
        const string email = "revoke-fail@example.com";
        var revokeFactory = CreateRevokeFailFactory();
        var memberId = await CreateMemberAsync(revokeFactory, email, "Revoke Fail", "google-revoke-fail");
        var admin = CreateAdminClient(revokeFactory, AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/members/{memberId}");
        var response = await admin.PostAsync(
            $"/admin/members/{memberId}/Suspend",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
                ["Reason"] = "Spam",
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/admin/members/{memberId}", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("/error/", response.Headers.Location.OriginalString, StringComparison.Ordinal);

        var page = await admin.GetStringAsync($"/admin/members/{memberId}");
        Assert.Contains(AdminMemberSuspendService.RevokeFailedMessage, page);
        Assert.True((await revokeFactory.Services.GetRequiredService<IMemberAccountRepository>().FindByIdAsync(memberId))!.IsSuspended);
    }

    [Fact]
    public async Task HideForumContent_DoesNotSuspendMember_AndUnhideRestoresContent()
    {
        const string email = "hide-only@example.com";
        await CreateSignedInMemberClientAsync(email, "Hide Only Spammer", "google-hide-only");
        var memberId = await GetMemberIdForEmailAsync(email);
        var forum = factory.Services.GetRequiredService<IForumWriteRepository>();
        await forum.CreateThreadAsync(new NewForumThread(
            1, memberId, "Hide Only Spammer", "Hide-only spam", "<p>spam</p>", DateTimeOffset.UtcNow));

        var admin = CreateAdminClient(AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/members/{memberId}");
        Assert.Contains("Hide all posts and threads", detail);
        var hide = await admin.PostAsync($"/admin/members/{memberId}/HideForumContent",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(detail),
            }));
        Assert.Equal(HttpStatusCode.Redirect, hide.StatusCode);
        Assert.False((await factory.Services.GetRequiredService<IMemberAccountRepository>().FindByIdAsync(memberId))!.IsSuspended);
        Assert.DoesNotContain("Hide-only spam", await factory.CreateClient().GetStringAsync("/api/v1/forum/categories/1/topics"));

        var hiddenDetail = await admin.GetStringAsync($"/admin/members/{memberId}");
        var unhide = await admin.PostAsync($"/admin/members/{memberId}/UnhideForumContent",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(hiddenDetail),
            }));
        Assert.Equal(HttpStatusCode.Redirect, unhide.StatusCode);
        Assert.Contains("Hide-only spam", await factory.CreateClient().GetStringAsync("/api/v1/forum/categories/1/topics"));
    }

    [Fact]
    public async Task ThreadHideAuthor_IsAdminOnly_AndHidesAllAuthorsContent()
    {
        await CreateSignedInMemberClientAsync("thread-spammer@example.com", "Thread Spammer", "google-thread-spammer");
        var spammerId = await GetMemberIdForEmailAsync("thread-spammer@example.com");
        var forum = factory.Services.GetRequiredService<IForumWriteRepository>();
        var spam = await forum.CreateThreadAsync(new NewForumThread(
            1, spammerId, "Thread Spammer", "Thread action spam", "<p>spam</p>", DateTimeOffset.UtcNow));
        var second = await forum.CreateThreadAsync(new NewForumThread(
            1, spammerId, "Thread Spammer", "Second spam thread", "<p>more spam</p>", DateTimeOffset.UtcNow));

        var ordinary = await CreateSignedInMemberClientAsync("ordinary@example.com", "Ordinary", "google-ordinary-hide");
        var ordinaryPage = await ordinary.GetStringAsync($"/forum/topic/{spam.TopicId}/thread-action-spam");
        var forbidden = await ordinary.PostAsync($"/forum/post/{spam.StarterPostId}/hide-author",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(ordinaryPage),
            }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var admin = CreateAdminClient(AdminEmail);
        var adminPage = await admin.GetStringAsync($"/forum/topic/{spam.TopicId}/thread-action-spam");
        Assert.Contains("Hide all posts by Thread Spammer", adminPage);
        var confirmation = await admin.GetStringAsync($"/forum/post/{spam.StarterPostId}/hide-author");
        Assert.Contains("2 posts", confirmation);
        Assert.Contains("2 started threads", confirmation);
        var hidden = await admin.PostAsync($"/forum/post/{spam.StarterPostId}/hide-author",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(confirmation),
            }));
        Assert.Equal(HttpStatusCode.Redirect, hidden.StatusCode);
        var topics = await factory.CreateClient().GetStringAsync("/api/v1/forum/categories/1/topics");
        Assert.DoesNotContain("Thread action spam", topics);
        Assert.DoesNotContain("Second spam thread", topics);
        _ = second;
    }

    private WebApplicationFactory<Program> CreateTimeoutHideFactory()
    {
        var timeout = SiteSearchSqlTimeoutTests.CreateSqlException(
            SiteSearchSqlTimeout.SqlErrorNumber,
            "Execution Timeout Expired. The timeout period elapsed prior to completion of the operation or the server is not responding.");
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IForumWriteRepository>();
                services.AddSingleton<IForumWriteRepository>(
                    new TimeoutHideForumWriteRepository(new InMemoryForumWriteRepository(), timeout));
            });
        });
    }

    private WebApplicationFactory<Program> CreateRevokeFailFactory() =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMobileAuthGrantRepository>();
                services.AddSingleton<IMobileAuthGrantRepository>(new ThrowingRevokeMobileAuthGrantRepository());
            });
        });

    private HttpClient CreateAdminClient(string? email = null) =>
        CreateAdminClient(factory, email);

    private static HttpClient CreateAdminClient(WebApplicationFactory<Program> host, string? email = null)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, email);
        }

        return client;
    }

    private Task<HttpClient> CreateSignedInMemberClientAsync(string email, string displayName, string subject) =>
        CreateSignedInMemberClientAsync(factory, email, displayName, subject);

    private static async Task<HttpClient> CreateSignedInMemberClientAsync(
        WebApplicationFactory<Program> host, string email, string displayName, string subject)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, displayName);

        var callbackResponse = await client.GetAsync("/account/external-login-callback");
        Assert.True(
            callbackResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
            $"Unexpected callback status code: {callbackResponse.StatusCode}");

        return client;
    }

    private async Task<Guid> GetMemberIdForEmailAsync(string email) =>
        await GetMemberIdForEmailAsync(factory, email);

    private static async Task<Guid> GetMemberIdForEmailAsync(WebApplicationFactory<Program> host, string email)
    {
        var members = host.Services.GetRequiredService<IMemberAccountRepository>();
        var account = await members.FindByEmailAsync(email);
        Assert.NotNull(account);
        return account!.Id;
    }

    private async Task<Guid> CreateMemberAsync(
        WebApplicationFactory<Program> host, string email, string displayName, string subject)
    {
        await CreateSignedInMemberClientAsync(host, email, displayName, subject);
        return await GetMemberIdForEmailAsync(host, email);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            html, """name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""");
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    private sealed class TimeoutHideForumWriteRepository(
        IForumWriteRepository inner,
        Exception timeout) : IForumWriteRepository
    {
        public Task<ForumThreadCreateResult> CreateThreadAsync(
            NewForumThread thread, CancellationToken cancellationToken = default) =>
            inner.CreateThreadAsync(thread, cancellationToken);

        public Task<int> CreatePostAsync(NewForumPost post, CancellationToken cancellationToken = default) =>
            inner.CreatePostAsync(post, cancellationToken);

        public Task<ForumEditablePost?> GetPostAsync(int postId, CancellationToken cancellationToken = default) =>
            inner.GetPostAsync(postId, cancellationToken);

        public Task<ForumPostUpdateResult> UpdatePostAsync(
            int postId,
            Guid editorMemberId,
            string sanitisedBody,
            bool isAdmin,
            int editWindowMinutes,
            DateTimeOffset? expectedUpdatedAt = null,
            CancellationToken cancellationToken = default) =>
            inner.UpdatePostAsync(
                postId, editorMemberId, sanitisedBody, isAdmin, editWindowMinutes, expectedUpdatedAt, cancellationToken);

        public Task<ForumWriteThread?> GetThreadAsync(int topicId, CancellationToken cancellationToken = default) =>
            inner.GetThreadAsync(topicId, cancellationToken);

        public Task<int> CountPostsByMemberSinceAsync(
            Guid memberId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
            inner.CountPostsByMemberSinceAsync(memberId, since, cancellationToken);

        public Task<int> CountApprovedPostsByMemberAsync(
            Guid memberId, CancellationToken cancellationToken = default) =>
            inner.CountApprovedPostsByMemberAsync(memberId, cancellationToken);

        public Task<ForumAuthorContentSummary> GetAuthorForumContentSummaryAsync(
            Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
            inner.GetAuthorForumContentSummaryAsync(memberId, displayName, cancellationToken);

        public Task<ForumAuthorContentSummary?> FindNoAccountForumAuthorAsync(
            string displayName, CancellationToken cancellationToken = default) =>
            inner.FindNoAccountForumAuthorAsync(displayName, cancellationToken);

        public Task HideAuthorForumContentAsync(
            Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
            throw timeout;

        public Task UnhideAuthorForumContentAsync(
            Guid? memberId, string displayName, CancellationToken cancellationToken = default) =>
            inner.UnhideAuthorForumContentAsync(memberId, displayName, cancellationToken);

        public Task<int> EnsureCategoryAsync(
            string slug, string name, CancellationToken cancellationToken = default) =>
            inner.EnsureCategoryAsync(slug, name, cancellationToken);
    }

    private sealed class ThrowingRevokeMobileAuthGrantRepository : IMobileAuthGrantRepository
    {
        public Task StoreAuthorizationCodeAsync(
            QueenZone.Data.Entities.MobileAuthAuthorizationCodeEntity code,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<QueenZone.Data.Entities.MobileAuthAuthorizationCodeEntity?> RedeemAuthorizationCodeAsync(
            string codeHash,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<QueenZone.Data.Entities.MobileAuthAuthorizationCodeEntity?>(null);

        public Task StoreRefreshTokenAsync(
            QueenZone.Data.Entities.MobileAuthRefreshTokenEntity token,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<QueenZone.Data.Entities.MobileAuthRefreshTokenEntity?> FindRefreshTokenByHashAsync(
            string tokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<QueenZone.Data.Entities.MobileAuthRefreshTokenEntity?>(null);

        public Task<bool> TryRevokeRefreshTokenAsync(
            string tokenHash,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<int> RevokeAllRefreshTokensForMemberAsync(
            Guid memberAccountId,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("token store unavailable");

        public Task<bool> LinkRefreshTokenRotationAsync(
            string oldTokenHash,
            string newTokenHash,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
