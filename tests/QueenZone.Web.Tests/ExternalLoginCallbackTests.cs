using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed partial class ExternalLoginCallbackTests : IClassFixture<ExternalCookieWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public ExternalLoginCallbackTests(ExternalCookieWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Callback_WithoutExternalCookie_RedirectsToLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/account/external-login-callback");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Callback_WithValidExternalCookie_SignsInAndGrantsMemberAccess()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        // These headers are read by ExternalCookieTestHandler to simulate what the OAuth
        // provider's redirect leaves behind in the ExternalCookie scheme.
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-subject-42");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, "googlefan@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Google Fan");

        var callbackResponse = await client.GetAsync("/account/external-login-callback");

        // Successful sign-in redirects away from the callback page (to "/" or returnUrl), not to login.
        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
        Assert.DoesNotContain("/account/login", callbackResponse.Headers.Location!.OriginalString);

        // MembersCookie is now stored in the cookie jar; member-probe should be accessible.
        var probeResponse = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.OK, probeResponse.StatusCode);
    }

    [Fact]
    public async Task Callback_WithAppleSubject_CreatesAppleExternalLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, MemberAuthenticationSchemes.Apple);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "apple-subject-42");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, "applefan@privaterelay.appleid.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Apple Fan");

        var response = await client.GetAsync("/account/external-login-callback");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await repository.FindByExternalLoginAsync(
            MemberAuthenticationSchemes.Apple,
            "apple-subject-42");
        Assert.NotNull(account);
        Assert.Equal("Apple Fan", account.DisplayName);
    }

    [Fact]
    public async Task Callback_WithAllowedAdminEmail_RequiresSeparateAdminAuthentication()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-admin-subject");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, AdminHttpTestHelpers.AdminEmail);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Test Admin");

        var callbackResponse = await client.GetAsync("/account/external-login-callback");
        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);

        var adminResponse = await client.GetAsync("/admin/news");
        Assert.Equal(HttpStatusCode.Unauthorized, adminResponse.StatusCode);

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, AdminHttpTestHelpers.AdminEmail);
        adminResponse = await client.GetAsync("/admin/news");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
    }

    [Fact]
    public async Task Callback_WithValidExternalCookie_HonoursSafeReturnUrl()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-subject-99");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, "returntest@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Return Test");

        var response = await client.GetAsync("/account/external-login-callback?returnUrl=%2Fforum");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forum", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Callback_WithValidExternalCookie_RejectsAbsoluteReturnUrl()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-subject-66");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, "openredirect@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Open Redirect Attempt");

        // An absolute URL must be rejected by the open-redirect guard and fall back to "/".
        var response = await client.GetAsync("/account/external-login-callback?returnUrl=https%3A%2F%2Fevil.example.com");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData("//evil.example.com")]
    [InlineData("//evil.example.com/phish")]
    [InlineData("/\\evil.example.com")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Callback_WithValidExternalCookie_RejectsProtocolRelativeAndEmptyReturnUrl(string returnUrl)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var subject = "google-subject-open-redirect-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(returnUrl)))[..12];
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, $"{subject}@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Protocol Relative Attempt");

        var response = await client.GetAsync(
            $"/account/external-login-callback?returnUrl={Uri.EscapeDataString(returnUrl)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain("evil.example.com", response.Headers.Location.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Callback_WithValidExternalCookie_HonoursNestedLocalReturnUrl()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-subject-forum-topic");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, "forumtopic@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Forum Topic Return");

        var response = await client.GetAsync("/account/external-login-callback?returnUrl=%2Fforum%2Ftopic%2F1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/forum/topic/1", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Callback_UnverifiedEmail_DoesNotLinkOrCreateASession()
    {
        await SeedPasswordAccountAsync("unverified-match@example.com", "S3curePass!", "Existing Fan");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-unverified-match");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, "unverified-match@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Existing Fan");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailVerifiedHeader, "false");

        var response = await client.GetAsync("/account/external-login-callback");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("externalEmail=unverified", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var login = await client.GetAsync(response.Headers.Location);
        var loginBody = await login.Content.ReadAsStringAsync();
        Assert.Contains(ExternalLoginMessages.UnverifiedEmail, loginBody, StringComparison.Ordinal);

        var probe = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.Redirect, probe.StatusCode);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        Assert.Null(await repository.FindByExternalLoginAsync("Google", "google-unverified-match"));
    }

    [Fact]
    public async Task Callback_VerifiedEmailMatch_RequiresConfirmationBeforeLinking()
    {
        const string email = "confirm-link@example.com";
        const string password = "S3curePass!";
        await SeedPasswordAccountAsync(email, password, "Confirm Fan");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-confirm-link");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Confirm Fan");

        var callback = await client.GetAsync("/account/external-login-callback?returnUrl=%2Fforum");

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/account/link-external-login", callback.Headers.Location!.OriginalString);
        var probe = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.Redirect, probe.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
            Assert.Null(await repository.FindByExternalLoginAsync("Google", "google-confirm-link"));
        }

        var confirmPage = await client.GetStringAsync("/account/link-external-login");
        Assert.Contains(email, confirmPage, StringComparison.Ordinal);
        Assert.Contains("will not link", confirmPage, StringComparison.Ordinal);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(confirmPage),
            ["password"] = password,
        });
        var confirmed = await client.PostAsync("/account/link-external-login?handler=Password", content);

        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.Equal("/forum", confirmed.Headers.Location!.OriginalString);
        var signedIn = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        using var linkedScope = factory.Services.CreateScope();
        var linkedRepository = linkedScope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        var account = await linkedRepository.FindByExternalLoginAsync("Google", "google-confirm-link");
        Assert.NotNull(account);
        Assert.Equal(email, account.Email);
    }

    [Fact]
    public async Task Callback_VerifiedEmailMatch_ConfirmsFromTheExistingAccountSession()
    {
        const string email = "session-confirm@example.com";
        const string password = "S3curePass!";
        await SeedPasswordAccountAsync(email, password, "Session Fan");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        var loginPage = await client.GetStringAsync("/account/login");
        using var login = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(loginPage),
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });
        var signedIn = await client.PostAsync("/account/login", login);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);

        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "GitHub");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "github-session-confirm");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Session Fan");
        var callback = await client.GetAsync("/account/external-login-callback?returnUrl=%2Fnews");
        Assert.Equal("/account/link-external-login", callback.Headers.Location!.OriginalString);

        var confirmPage = await client.GetStringAsync("/account/link-external-login");
        Assert.Contains("Link GitHub to this account", confirmPage, StringComparison.Ordinal);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(confirmPage),
        });
        var confirmed = await client.PostAsync("/account/link-external-login?handler=Confirm", content);

        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.Equal("/news", confirmed.Headers.Location!.OriginalString);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        Assert.NotNull(await repository.FindByExternalLoginAsync("GitHub", "github-session-confirm"));
    }

    [Fact]
    public async Task LinkPage_WithoutPendingCookie_RedirectsToExpiredLogin()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/account/link-external-login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("externalLink=expired", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var login = await client.GetAsync(response.Headers.Location);
        var body = await login.Content.ReadAsStringAsync();
        Assert.Contains(ExternalLoginMessages.ExpiredLink, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_WrongPassword_DoesNotLink()
    {
        const string email = "wrong-password-link@example.com";
        await SeedPasswordAccountAsync(email, "S3curePass!", "Wrong Password Fan");
        var client = CreateExternalClient("Google", "google-wrong-password", email, "Wrong Password Fan");

        var callback = await client.GetAsync("/account/external-login-callback");
        Assert.Equal("/account/link-external-login", callback.Headers.Location!.OriginalString);
        var confirmPage = await client.GetStringAsync("/account/link-external-login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(confirmPage),
            ["password"] = "not-the-password",
        });
        var confirmed = await client.PostAsync("/account/link-external-login?handler=Password", content);

        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        var body = await confirmed.Content.ReadAsStringAsync();
        Assert.Contains("Incorrect email or password.", body, StringComparison.Ordinal);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        Assert.Null(await repository.FindByExternalLoginAsync("Google", "google-wrong-password"));
    }

    [Fact]
    public async Task Callback_Cancel_ClearsThePendingLink()
    {
        const string email = "cancel-link@example.com";
        await SeedPasswordAccountAsync(email, "S3curePass!", "Cancel Fan");
        var client = CreateExternalClient("Google", "google-cancel-link", email, "Cancel Fan");
        var callback = await client.GetAsync("/account/external-login-callback");
        Assert.Equal("/account/link-external-login", callback.Headers.Location!.OriginalString);
        var confirmPage = await client.GetStringAsync("/account/link-external-login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(confirmPage),
        });

        var cancelled = await client.PostAsync("/account/link-external-login?handler=Cancel", content);

        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        Assert.Contains("/account/login", cancelled.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var again = await client.GetAsync("/account/link-external-login");
        Assert.Contains("externalLink=expired", again.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_SecondProvider_DoesNotReplaceThePendingLink()
    {
        const string email = "pending-owner@example.com";
        const string other = "pending-other@example.com";
        await SeedPasswordAccountAsync(email, "S3curePass!", "Pending Owner");
        await SeedPasswordAccountAsync(other, "S3curePass!", "Pending Other");
        var client = CreateExternalClient("Google", "google-pending-owner", email, "Pending Owner");
        var callback = await client.GetAsync("/account/external-login-callback");
        Assert.Equal("/account/link-external-login", callback.Headers.Location!.OriginalString);

        client.DefaultRequestHeaders.Remove(ExternalCookieTestHandler.ProviderHeader);
        client.DefaultRequestHeaders.Remove(ExternalCookieTestHandler.SubjectHeader);
        client.DefaultRequestHeaders.Remove(ExternalCookieTestHandler.EmailHeader);
        client.DefaultRequestHeaders.Remove(ExternalCookieTestHandler.NameHeader);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "GitHub");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "github-pending-other");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, other);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Pending Other");
        var second = await client.GetAsync("/account/external-login-callback");

        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Contains("linkError=provider", second.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var page = await client.GetStringAsync(second.Headers.Location);
        Assert.Contains(email, page, StringComparison.Ordinal);
        Assert.Contains(ExternalLoginMessages.ProviderMismatch, page, StringComparison.Ordinal);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        Assert.Null(await repository.FindByExternalLoginAsync("Google", "google-pending-owner"));
        Assert.Null(await repository.FindByExternalLoginAsync("GitHub", "github-pending-other"));
    }

    [Fact]
    public async Task Callback_VerifiedEmail_DoesNotSignInASuspendedAccount()
    {
        const string email = "suspended-callback@example.com";
        await SeedPasswordAccountAsync(email, "S3curePass!", "Suspended Fan");
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
            var account = await repository.FindByEmailAsync(email);
            Assert.NotNull(account);
            await repository.SuspendAsync(account.Id, "Spamming the board", "admin@queenzone.org", DateTime.UtcNow);
        }

        var client = CreateExternalClient("Google", "google-suspended-callback", email, "Suspended Fan");
        var response = await client.GetAsync("/account/external-login-callback");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("suspended=1", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        using var linked = factory.Services.CreateScope();
        var accounts = linked.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        Assert.Null(await accounts.FindByExternalLoginAsync("Google", "google-suspended-callback"));
    }

    [Fact]
    public async Task Callback_ReturningProviderSubject_SignsInWhenEmailIsUnverified()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-returning-unverified");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, "returning-unverified@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Returning Fan");

        var created = await client.GetAsync("/account/external-login-callback");
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.DoesNotContain("/account/login", created.Headers.Location!.OriginalString, StringComparison.Ordinal);

        client.DefaultRequestHeaders.Remove(ExternalCookieTestHandler.EmailVerifiedHeader);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailVerifiedHeader, "false");
        var returned = await client.GetAsync("/account/external-login-callback?returnUrl=%2Fnews");

        Assert.Equal(HttpStatusCode.Redirect, returned.StatusCode);
        Assert.Equal("/news", returned.Headers.Location!.OriginalString);
    }

    private HttpClient CreateExternalClient(string provider, string subject, string email, string name)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, provider);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, name);
        return client;
    }

    private async Task SeedPasswordAccountAsync(string email, string password, string displayName)
    {
        using var scope = factory.Services.CreateScope();
        var memberAccountService = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
        var result = await memberAccountService.RegisterAsync(email, password, displayName);
        Assert.True(result.Succeeded, result.Error);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [System.Text.RegularExpressions.GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex AntiforgeryTokenRegex();
}

/// <summary>
/// Test double for the ExternalCookie authentication scheme. Reads claims from request
/// headers instead of a real OAuth-backed cookie, so integration tests can drive
/// ExternalLoginCallback without a live OAuth provider.
/// Extends SignOutAuthenticationHandler so that SignOutAsync (called by the callback page
/// to clean up the external cookie after sign-in) succeeds without a real cookie store.
/// </summary>
internal sealed class ExternalCookieTestHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : SignOutAuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string ProviderHeader = "X-Test-External-Provider";
    public const string SubjectHeader = "X-Test-External-Subject";
    public const string EmailHeader = "X-Test-External-Email";
    public const string NameHeader = "X-Test-External-Name";

    /// <summary>
    /// When <c>false</c>, the simulated provider does not confirm the email.
    /// Absent or any other value means the provider verified it.
    /// </summary>
    public const string EmailVerifiedHeader = "X-Test-External-Email-Verified";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(EmailHeader, out var emailValues))
            return Task.FromResult(AuthenticateResult.NoResult());

        var provider = Request.Headers[ProviderHeader].FirstOrDefault() ?? "Google";
        var subject = Request.Headers[SubjectHeader].FirstOrDefault() ?? "test-subject";
        var email = emailValues.First()!;
        var name = Request.Headers[NameHeader].FirstOrDefault() ?? email;
        var verified = !string.Equals(
            Request.Headers[EmailVerifiedHeader].FirstOrDefault(),
            "false",
            StringComparison.OrdinalIgnoreCase);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.Name, name),
        };
        if (verified)
        {
            var claimType = string.Equals(provider, MemberAuthenticationSchemes.Discord, StringComparison.OrdinalIgnoreCase)
                ? ExternalLoginEmail.DiscordVerifiedClaimType
                : ExternalLoginEmail.EmailVerifiedClaimType;
            claims.Add(new Claim(claimType, "true"));
        }

        // AuthenticationType becomes the provider name read by ExternalLoginCallback.
        var identity = new ClaimsIdentity(claims, authenticationType: provider);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    // No-op: the real ExternalCookie removes its cookie from the browser, but in tests
    // there is no real cookie jar entry to clean up.
    protected override Task HandleSignOutAsync(AuthenticationProperties? properties) =>
        Task.CompletedTask;
}
