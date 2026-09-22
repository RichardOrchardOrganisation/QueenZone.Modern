using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Web.Tests;

/// <summary>
/// Covers <c>grant_type=password</c> on <c>POST /api/v1/auth/token</c> (Bob Option A / #1351).
/// Accounts are provisioned the same way as website password sign-in: out-of-band
/// <see cref="MemberAccountService.RegisterAsync"/>, not a mobile registration UI.
/// </summary>
public sealed class MobileAuthPasswordGrantTests
{
    [Fact]
    public async Task PasswordGrant_IssuesSameTokenShape_AndOpensSession()
    {
        using var factory = new QueenZoneWebApplicationFactory();
        await SeedAccountAsync(factory, "mobile-reviewer@example.com", "correct horse battery staple", "App Reviewer");
        using var client = factory.CreateAnonymousClient();

        using var request = PasswordForm("mobile-reviewer@example.com", "correct horse battery staple");
        var response = await client.PostAsync(MobileAuthEndpoints.TokenPath, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = payload.GetProperty("access_token").GetString();
        var refreshToken = payload.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));
        Assert.Equal("Bearer", payload.GetProperty("token_type").GetString());
        Assert.True(payload.GetProperty("expires_in").GetInt32() > 0);
        Assert.DoesNotContain("correct horse battery staple", await response.Content.ReadAsStringAsync());

        using var sessionClient = factory.CreateAnonymousClient();
        sessionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var session = await sessionClient.GetFromJsonAsync<JsonElement>(MobileAuthEndpoints.SessionPath);
        Assert.Equal("mobile-reviewer@example.com", session.GetProperty("email").GetString());
        Assert.Equal("App Reviewer", session.GetProperty("displayName").GetString());

        using var refreshRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = MobileAuthOptions.DefaultClientId,
            ["refresh_token"] = refreshToken!,
        });
        var refresh = await client.PostAsync(MobileAuthEndpoints.TokenPath, refreshRequest);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var refreshed = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(refreshed.GetProperty("access_token").GetString()));
        Assert.NotEqual(refreshToken, refreshed.GetProperty("refresh_token").GetString());
    }

    [Fact]
    public async Task PasswordGrant_WrongPassword_ReturnsGenericInvalidGrant()
    {
        using var factory = new QueenZoneWebApplicationFactory();
        await SeedAccountAsync(factory, "mobile-wrong@example.com", "the-real-password-123", "Wrong Pass");
        using var client = factory.CreateAnonymousClient();

        using var request = PasswordForm("mobile-wrong@example.com", "not-the-password");
        var response = await client.PostAsync(MobileAuthEndpoints.TokenPath, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_grant", payload.GetProperty("error").GetString());
        Assert.Equal(MobileAuthService.PasswordGrantInvalidDescription, payload.GetProperty("error_description").GetString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("mobile-wrong@example.com", body, StringComparison.Ordinal);
        Assert.DoesNotContain("the-real-password-123", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordGrant_UnknownEmail_ReturnsSameInvalidGrantAsWrongPassword()
    {
        using var factory = new QueenZoneWebApplicationFactory();
        using var client = factory.CreateAnonymousClient();

        using var request = PasswordForm("nobody@example.com", "whatever");
        var response = await client.PostAsync(MobileAuthEndpoints.TokenPath, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_grant", payload.GetProperty("error").GetString());
        Assert.Equal(MobileAuthService.PasswordGrantInvalidDescription, payload.GetProperty("error_description").GetString());
        Assert.DoesNotContain("nobody@example.com", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordGrant_SuspendedAccount_ReturnsTheSameInvalidGrantAsAWrongPassword()
    {
        using var factory = new QueenZoneWebApplicationFactory();
        var account = await SeedAccountAsync(
            factory,
            "mobile-suspended@example.com",
            "correct horse battery staple",
            "Suspended Reviewer");
        using (var scope = factory.Services.CreateScope())
        {
            var members = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
            await members.SuspendAsync(
                account.Id,
                "Review hold",
                AdminHttpTestHelpers.AdminEmail,
                DateTime.UtcNow);
        }

        using var client = factory.CreateAnonymousClient();
        using var request = PasswordForm("mobile-suspended@example.com", "correct horse battery staple");
        var response = await client.PostAsync(MobileAuthEndpoints.TokenPath, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_grant", payload.GetProperty("error").GetString());
        Assert.Equal(MobileAuthService.PasswordGrantInvalidDescription, payload.GetProperty("error_description").GetString());
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("suspended", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mobile-suspended@example.com", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordGrant_WrongClientId_ReturnsInvalidGrant()
    {
        using var factory = new QueenZoneWebApplicationFactory();
        await SeedAccountAsync(factory, "mobile-client@example.com", "correct horse battery staple", "Client Fan");
        using var client = factory.CreateAnonymousClient();
        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "not-queenzone-mobile",
            ["username"] = "mobile-client@example.com",
            ["password"] = "correct horse battery staple",
        });

        var response = await client.PostAsync(MobileAuthEndpoints.TokenPath, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_grant", payload.GetProperty("error").GetString());
        Assert.Equal(MobileAuthService.PasswordGrantInvalidDescription, payload.GetProperty("error_description").GetString());
    }

    private static FormUrlEncodedContent PasswordForm(string username, string password) =>
        new(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = MobileAuthOptions.DefaultClientId,
            ["username"] = username,
            ["password"] = password,
        });

    private static async Task<QueenZone.Data.Entities.MemberAccount> SeedAccountAsync(
        QueenZoneWebApplicationFactory factory,
        string email,
        string password,
        string displayName)
    {
        using var scope = factory.Services.CreateScope();
        var members = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
        var result = await members.RegisterAsync(email, password, displayName);
        Assert.True(result.Succeeded, result.Error);
        Assert.NotNull(result.Account);
        return result.Account;
    }
}
