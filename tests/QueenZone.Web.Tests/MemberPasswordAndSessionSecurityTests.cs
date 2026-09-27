using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Password lockout wording, leftover member cookies, and bearer rejection after
/// suspend or deletion. A new sign-in during the deletion cooling-off period still works.
/// </summary>
public sealed partial class MemberPasswordAndSessionSecurityTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public MemberPasswordAndSessionSecurityTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task LoginPage_DoesNotSaySuspended_ForABadPasswordOnASuspendedAccount()
    {
        const string email = "locked-wording@example.com";
        var account = await SeedPasswordAccountAsync(email, "correct horse battery staple", "Suspended Reviewer");
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<MemberAccountService>().SuspendAsync(
                account.Id,
                "Review hold",
                "admin@queenzone.org",
                DateTime.UtcNow);
        }

        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        var loginPage = await client.GetStringAsync("/account/login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(loginPage),
            ["Input.Email"] = email,
            ["Input.Password"] = "not-the-password",
        });

        var response = await client.PostAsync("/account/login", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(MemberAccountService.InvalidPasswordSignInError, body, StringComparison.Ordinal);
        Assert.DoesNotContain("suspended", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecondBrowserCookie_StopsOnTheNextRequestAfterDelete_AndANewSignInCanCancel()
    {
        const string email = "two-browsers@example.com";
        await SeedPasswordAccountAsync(email, "correct horse battery staple", "Two Browsers");
        using var first = factory.CreateAnonymousClient(allowAutoRedirect: false);
        using var second = factory.CreateAnonymousClient(allowAutoRedirect: false);
        await SignInAsync(first, email, "correct horse battery staple");
        await SignInAsync(second, email, "correct horse battery staple");
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/account/member-probe")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/account/member-probe")).StatusCode);

        var deletePage = await first.GetStringAsync("/account/delete");
        var deleted = await first.PostAsync(
            "/account/delete",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = ExtractAntiforgeryToken(deletePage),
                ["Confirmation"] = "DELETE",
            }));
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);

        var leftover = await second.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.Redirect, leftover.StatusCode);
        Assert.Contains("/account/login", leftover.Headers.Location!.OriginalString, StringComparison.Ordinal);

        await SignInAsync(second, email, "correct horse battery staple");
        var cancelPage = await second.GetStringAsync("/account/delete");
        Assert.Contains("Cancel account deletion", cancelPage, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync("/account/member-probe")).StatusCode);
    }

    [Fact]
    public async Task BearerToken_IsRejectedAfterSuspend_AndRefreshFails_WhileAnActiveMemberStillWorks()
    {
        var suspended = await SeedPasswordAccountAsync(
            "bearer-suspend@example.com",
            "correct horse battery staple",
            "Bearer Suspended");
        var active = await SeedPasswordAccountAsync(
            "bearer-active@example.com",
            "correct horse battery staple",
            "Bearer Active");
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var suspendedGrant = await PasswordGrantAsync(client, "bearer-suspend@example.com", "correct horse battery staple");
        var activeGrant = await PasswordGrantAsync(client, "bearer-active@example.com", "correct horse battery staple");
        Assert.Equal(HttpStatusCode.OK, (await SessionAsync(client, activeGrant.AccessToken)).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<MemberAccountService>().SuspendAsync(
                suspended.Id,
                "Review hold",
                "admin@queenzone.org",
                DateTime.UtcNow);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionAsync(client, suspendedGrant.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SessionAsync(client, activeGrant.AccessToken)).StatusCode);

        using var refresh = RefreshForm(suspendedGrant.RefreshToken);
        var refreshResponse = await client.PostAsync(MobileAuthEndpoints.TokenPath, refresh);
        Assert.Equal(HttpStatusCode.BadRequest, refreshResponse.StatusCode);
        var refreshBody = await refreshResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_grant", refreshBody.GetProperty("error").GetString());

        using var scopeAfter = factory.Services.CreateScope();
        var issuer = scopeAfter.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var reissued = issuer.IssueAccessToken(suspended.Id, suspended.Email, suspended.DisplayName);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionAsync(client, reissued)).StatusCode);
    }

    [Fact]
    public async Task BearerToken_IssuedBeforeDeletion_IsRejected_AndALaterTokenCanStillCallTheApi()
    {
        var account = await SeedPasswordAccountAsync(
            "bearer-delete@example.com",
            "correct horse battery staple",
            "Bearer Delete");
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        var grant = await PasswordGrantAsync(client, "bearer-delete@example.com", "correct horse battery staple");
        Assert.Equal(HttpStatusCode.OK, (await SessionAsync(client, grant.AccessToken)).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", grant.AccessToken);
        var deleted = await client.PostAsJsonAsync(
            $"{MeApiEndpoints.Path}/deletion-request",
            new { confirmation = "DELETE" });
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        Assert.Equal(HttpStatusCode.Unauthorized, (await SessionAsync(client, grant.AccessToken)).StatusCode);
        using var refresh = RefreshForm(grant.RefreshToken);
        var refreshResponse = await client.PostAsync(MobileAuthEndpoints.TokenPath, refresh);
        Assert.Equal(HttpStatusCode.BadRequest, refreshResponse.StatusCode);

        await WaitUntilClockMovesAsync();
        using var scope = factory.Services.CreateScope();
        var issuer = scope.ServiceProvider.GetRequiredService<MobileAuthTokenIssuer>();
        var later = issuer.IssueAccessToken(account.Id, account.Email, "Deleted member");
        Assert.Equal(HttpStatusCode.OK, (await SessionAsync(client, later)).StatusCode);
    }

    private async Task<QueenZone.Data.Entities.MemberAccount> SeedPasswordAccountAsync(
        string email,
        string password,
        string displayName)
    {
        using var scope = factory.Services.CreateScope();
        var members = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
        var result = await members.RegisterAsync(email, password, displayName);
        Assert.True(result.Succeeded, result.Error);
        return result.Account!;
    }

    private static async Task WaitUntilClockMovesAsync()
    {
        var start = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        while (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() <= start)
        {
            await Task.Delay(1);
        }
    }

    private static async Task SignInAsync(HttpClient client, string email, string password)
    {
        var loginPage = await client.GetStringAsync("/account/login");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = ExtractAntiforgeryToken(loginPage),
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });
        var response = await client.PostAsync("/account/login", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task<(string AccessToken, string RefreshToken)> PasswordGrantAsync(
        HttpClient client,
        string username,
        string password)
    {
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = MobileAuthOptions.DefaultClientId,
            ["username"] = username,
            ["password"] = password,
        });
        var response = await client.PostAsync(MobileAuthEndpoints.TokenPath, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (payload.GetProperty("access_token").GetString()!, payload.GetProperty("refresh_token").GetString()!);
    }

    private static async Task<HttpResponseMessage> SessionAsync(HttpClient client, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MobileAuthEndpoints.SessionPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request);
    }

    private static FormUrlEncodedContent RefreshForm(string refreshToken) =>
        new(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = MobileAuthOptions.DefaultClientId,
            ["refresh_token"] = refreshToken,
        });

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
