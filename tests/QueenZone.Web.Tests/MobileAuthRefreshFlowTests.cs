using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace QueenZone.Web.Tests;

public sealed class MobileAuthRefreshFlowTests
{
    private static QueenZoneWebApplicationFactory CreateFactory(TimeProvider? timeProvider = null) =>
        QueenZoneWebApplicationFactory.WithServices(services =>
        {
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, ExternalCookieTestHandler>(
                    MemberAuthenticationSchemes.ExternalCookie, _ => { });

            foreach (var provider in MemberAuthenticationSchemes.ExternalProviders)
            {
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestOAuthProviderHandler>(provider, _ => { });
            }

            if (timeProvider is not null)
            {
                services.AddSingleton(timeProvider);
            }
        });

    [Fact]
    public async Task RefreshGrant_IssuesNewAccessToken_AndRejectsReuseAfterTheGraceWindow()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var factory = CreateFactory(time);
        var issued = await CompletePkceAsync(factory, "refresh-fan@example.com", "google-refresh-1");

        using var refreshRequest = RefreshForm(issued.RefreshToken);
        var refreshResponse = await issued.Client.PostAsync(MobileAuthEndpoints.TokenPath, refreshRequest);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await ReadTokenPayloadAsync(refreshResponse);
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
        Assert.NotEqual(issued.RefreshToken, refreshed.RefreshToken);
        Assert.DoesNotContain(issued.RefreshToken, await refreshResponse.Content.ReadAsStringAsync());

        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        var session = await sessionClient.GetAsync(MobileAuthEndpoints.SessionPath);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        // Past the reuse grace window, replaying the rotated-away token is theft,
        // not a client that lost its rotation response.
        time.Advance(TimeSpan.FromSeconds(301));
        using var reusedRequest = RefreshForm(issued.RefreshToken);
        var reused = await issued.Client.PostAsync(MobileAuthEndpoints.TokenPath, reusedRequest);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        var body = await reused.Content.ReadAsStringAsync();
        Assert.Contains("invalid_grant", body, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.RefreshToken, body);
        Assert.DoesNotContain(refreshed.RefreshToken, body);
    }

    [Fact]
    public async Task RefreshGrant_ReplayedImmediately_RecoversInsteadOfSigningOut()
    {
        // Mirrors an iOS app relaunch/restart that kills the process right as a
        // refresh response arrives: the client never sees the new refresh token
        // and retries with the one it already rotated away. That must recover a
        // fresh, working pair rather than revoke every device.
        using var factory = CreateFactory();
        var issued = await CompletePkceAsync(factory, "lost-response-fan@example.com", "google-lost-response-1");

        using var firstRequest = RefreshForm(issued.RefreshToken);
        var first = await issued.Client.PostAsync(MobileAuthEndpoints.TokenPath, firstRequest);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var retriedRequest = RefreshForm(issued.RefreshToken);
        var retried = await issued.Client.PostAsync(MobileAuthEndpoints.TokenPath, retriedRequest);
        Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        var recovered = await ReadTokenPayloadAsync(retried);
        Assert.False(string.IsNullOrWhiteSpace(recovered.AccessToken));

        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", recovered.AccessToken);
        var session = await sessionClient.GetAsync(MobileAuthEndpoints.SessionPath);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    }

    [Fact]
    public async Task Revoke_ThenRefresh_ReturnsInvalidGrantWithoutEchoingToken()
    {
        using var factory = CreateFactory();
        var issued = await CompletePkceAsync(factory, "revoke-fan@example.com", "google-revoke-1");

        using var revokeRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = issued.RefreshToken,
            ["token_type_hint"] = "refresh_token",
        });
        var revoke = await issued.Client.PostAsync(MobileAuthEndpoints.RevokePath, revokeRequest);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        var revokeBody = await revoke.Content.ReadAsStringAsync();
        Assert.DoesNotContain(issued.RefreshToken, revokeBody);

        using var refreshRequest = RefreshForm(issued.RefreshToken);
        var refresh = await issued.Client.PostAsync(MobileAuthEndpoints.TokenPath, refreshRequest);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.DoesNotContain(issued.RefreshToken, await refresh.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Logout_RevokesAllRefreshTokensForMember()
    {
        using var factory = CreateFactory();
        var issued = await CompletePkceAsync(factory, "logout-fan@example.com", "google-logout-1");

        using var logoutClient = factory.CreateClient();
        logoutClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", issued.AccessToken);
        var logout = await logoutClient.PostAsync(MobileAuthEndpoints.LogoutPath, null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var refreshRequest = RefreshForm(issued.RefreshToken);
        var refresh = await issued.Client.PostAsync(MobileAuthEndpoints.TokenPath, refreshRequest);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
    }

    [Fact]
    public async Task ExpiredAccessToken_IsRejectedBySessionEndpoint()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);
        var expired = CreateExpiredAccessToken(Guid.NewGuid(), "expired@example.com", "Expired");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired);

        var session = await client.GetAsync(MobileAuthEndpoints.SessionPath);

        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Fact]
    public async Task AdminSuspend_RevokesRefreshTokensImmediately()
    {
        using var factory = CreateFactory();
        var issued = await CompletePkceAsync(factory, "suspend-fan@example.com", "google-suspend-1");

        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", issued.AccessToken);
        var session = await sessionClient.GetFromJsonAsync<JsonElement>(MobileAuthEndpoints.SessionPath);
        var memberId = Guid.Parse(session.GetProperty("memberId").GetString()!);

        var admin = AdminHttpTestHelpers.CreateClient(factory, AdminHttpTestHelpers.AdminEmail);
        var detail = await admin.GetStringAsync($"/admin/members/{memberId}");
        var tokenMatch = System.Text.RegularExpressions.Regex.Match(
            detail, """name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""");
        Assert.True(tokenMatch.Success);
        var suspend = await admin.PostAsync(
            $"/admin/members/{memberId}/Suspend",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = tokenMatch.Groups["token"].Value,
                ["Reason"] = "Compromised account",
            }));
        Assert.Equal(HttpStatusCode.Redirect, suspend.StatusCode);

        using var refreshRequest = RefreshForm(issued.RefreshToken);
        var refresh = await issued.Client.PostAsync(MobileAuthEndpoints.TokenPath, refreshRequest);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
        Assert.DoesNotContain(issued.RefreshToken, await refresh.Content.ReadAsStringAsync());
    }

    private static async Task<(HttpClient Client, string AccessToken, string RefreshToken)> CompletePkceAsync(
        QueenZoneWebApplicationFactory factory,
        string email,
        string subject)
    {
        var pair = MobileAuthPkceTestData.CreatePair();
        const string state = "refresh-csrf";
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, MemberAuthenticationSchemes.Google);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Refresh Fan");

        var authorize = await client.GetAsync(
            $"{MobileAuthEndpoints.AuthorizePath}?response_type=code" +
            $"&client_id={MobileAuthOptions.DefaultClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(MobileAuthPkceTestData.RedirectUri)}" +
            $"&code_challenge={pair.Challenge}&code_challenge_method=S256" +
            $"&state={state}&provider=Google");
        var callback = await client.GetAsync(authorize.Headers.Location!);
        var query = QueryHelpers.ParseQuery(callback.Headers.Location!.Query);
        using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = MobileAuthOptions.DefaultClientId,
            ["redirect_uri"] = MobileAuthPkceTestData.RedirectUri,
            ["code"] = query["code"].ToString(),
            ["code_verifier"] = pair.Verifier,
        });
        var tokenResponse = await client.PostAsync(MobileAuthEndpoints.TokenPath, tokenRequest);
        tokenResponse.EnsureSuccessStatusCode();
        var payload = await ReadTokenPayloadAsync(tokenResponse);
        return (client, payload.AccessToken, payload.RefreshToken);
    }

    private static FormUrlEncodedContent RefreshForm(string refreshToken) =>
        new(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = MobileAuthOptions.DefaultClientId,
            ["refresh_token"] = refreshToken,
        });

    private static async Task<(string AccessToken, string RefreshToken)> ReadTokenPayloadAsync(
        HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (
            payload.GetProperty("access_token").GetString()!,
            payload.GetProperty("refresh_token").GetString()!);
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }

    private static string CreateExpiredAccessToken(Guid memberId, string email, string displayName)
    {
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("testing-mobile-auth-signing-key-32b!"));
        var token = new JwtSecurityToken(
            issuer: "https://www.queenzone.org",
            audience: MobileAuthOptions.DefaultClientId,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, memberId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, memberId.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, displayName),
            ],
            notBefore: now.AddHours(-2),
            expires: now.AddHours(-1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
