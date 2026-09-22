using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class MobileOAuthPkceFlowTests
{
    private static QueenZoneWebApplicationFactory CreateFactory() =>
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
        });

    [Theory]
    [InlineData(MemberAuthenticationSchemes.Google)]
    [InlineData(MemberAuthenticationSchemes.Microsoft)]
    [InlineData(MemberAuthenticationSchemes.Discord)]
    [InlineData(MemberAuthenticationSchemes.GitHub)]
    [InlineData(MemberAuthenticationSchemes.Apple)]
    public async Task PkceFlow_ReturnsAccessAndRefreshTokens_WithoutMemberCookie(string provider)
    {
        using var factory = CreateFactory();
        var pair = MobileAuthPkceTestData.CreatePair();
        const string state = "mobile-csrf-state";
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, provider);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, $"{provider.ToLowerInvariant()}-subject-720");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, $"{provider.ToLowerInvariant()}720@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, $"{provider} Fan");

        var authorize = await client.GetAsync(AuthorizeUrl(provider, pair.Challenge, state));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        authorize.AssertDoesNotContainMemberCookie();
        var callbackLocation = authorize.Headers.Location;
        Assert.NotNull(callbackLocation);
        Assert.StartsWith(MobileAuthEndpoints.CallbackPath, callbackLocation!.OriginalString, StringComparison.Ordinal);

        var callback = await client.GetAsync(callbackLocation);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        callback.AssertDoesNotContainMemberCookie();
        var appRedirect = callback.Headers.Location;
        Assert.NotNull(appRedirect);
        Assert.Equal("queenzone", appRedirect!.Scheme);
        var query = QueryHelpers.ParseQuery(appRedirect.Query);
        Assert.Equal(state, query["state"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(query["code"].ToString()));
        Assert.False(query.ContainsKey("error"));

        using var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = MobileAuthOptions.DefaultClientId,
            ["redirect_uri"] = MobileAuthPkceTestData.RedirectUri,
            ["code"] = query["code"].ToString(),
            ["code_verifier"] = pair.Verifier,
        });
        var tokenResponse = await client.PostAsync(MobileAuthEndpoints.TokenPath, tokenRequest);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        tokenResponse.AssertDoesNotContainMemberCookie();

        var payload = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = payload.GetProperty("access_token").GetString();
        var refreshToken = payload.GetProperty("refresh_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));
        Assert.Equal("Bearer", payload.GetProperty("token_type").GetString());
        Assert.True(payload.GetProperty("expires_in").GetInt32() > 0);

        using var sessionClient = factory.CreateClient();
        sessionClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var session = await sessionClient.GetAsync(MobileAuthEndpoints.SessionPath);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        var sessionPayload = await session.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal($"{provider.ToLowerInvariant()}720@example.com", sessionPayload.GetProperty("email").GetString());

        var cookieProbe = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.Redirect, cookieProbe.StatusCode);
        Assert.Contains("/account/login", cookieProbe.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_VerifiedEmailMatch_ConfirmsOnTheWebsiteBeforeIssuingACode()
    {
        using var factory = CreateFactory();
        const string email = "mobile-confirm@example.com";
        const string password = "S3curePass!";
        using (var scope = factory.Services.CreateScope())
        {
            var members = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
            var registered = await members.RegisterAsync(email, password, "Mobile Confirm");
            Assert.True(registered.Succeeded, registered.Error);
        }

        var pair = MobileAuthPkceTestData.CreatePair();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, MemberAuthenticationSchemes.Google);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-mobile-confirm-route");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, email);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Mobile Confirm");

        var authorize = await client.GetAsync(AuthorizeUrl(MemberAuthenticationSchemes.Google, pair.Challenge, "mobile-confirm-state"));
        var callback = await client.GetAsync(authorize.Headers.Location);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/account/link-external-login", callback.Headers.Location!.OriginalString);
        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
            Assert.Null(await repository.FindByExternalLoginAsync(MemberAuthenticationSchemes.Google, "google-mobile-confirm-route"));
        }

        var confirmPage = await client.GetStringAsync("/account/link-external-login");
        var token = Regex.Match(
            confirmPage,
            """name="__RequestVerificationToken"[^>]*value="(?<token>[^"]+)""",
            RegexOptions.IgnoreCase).Groups["token"].Value;
        Assert.False(string.IsNullOrWhiteSpace(token));
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["password"] = password,
        });
        var confirmed = await client.PostAsync("/account/link-external-login?handler=Password", content);

        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.Equal("queenzone", confirmed.Headers.Location!.Scheme);
        var query = QueryHelpers.ParseQuery(confirmed.Headers.Location.Query);
        Assert.Equal("mobile-confirm-state", query["state"].ToString());
        Assert.False(string.IsNullOrWhiteSpace(query["code"].ToString()));
        using var linkedScope = factory.Services.CreateScope();
        var linkedRepository = linkedScope.ServiceProvider.GetRequiredService<IMemberAccountRepository>();
        Assert.NotNull(await linkedRepository.FindByExternalLoginAsync(
            MemberAuthenticationSchemes.Google,
            "google-mobile-confirm-route"));
    }

    [Fact]
    public async Task Authorize_UnknownProvider_RedirectsWithError()
    {
        using var factory = CreateFactory();
        var pair = MobileAuthPkceTestData.CreatePair();
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync(AuthorizeUrl("Facebook", pair.Challenge, "st"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("queenzone", response.Headers.Location!.Scheme);
        var query = QueryHelpers.ParseQuery(response.Headers.Location.Query);
        Assert.Equal("invalid_request", query["error"].ToString());
        response.AssertDoesNotContainMemberCookie();
    }

    [Fact]
    public async Task Authorize_UnregisteredRedirect_ReturnsJsonError()
    {
        using var factory = CreateFactory();
        var pair = MobileAuthPkceTestData.CreatePair();
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync(
            $"{MobileAuthEndpoints.AuthorizePath}?response_type=code&client_id={MobileAuthOptions.DefaultClientId}" +
            $"&redirect_uri={Uri.EscapeDataString("https://evil.example/cb")}" +
            $"&code_challenge={pair.Challenge}&code_challenge_method=S256&state=st&provider=Google");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_request", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Token_WithoutFormContent_ReturnsInvalidRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.PostAsync(
            MobileAuthEndpoints.TokenPath,
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_request", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Session_WithoutBearerToken_ReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync(MobileAuthEndpoints.SessionPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WebLoginPage_StillRenders()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateAnonymousClient();

        var response = await client.GetAsync("/account/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Google", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Callback_WithoutExternalLogin_ReturnsAccessDenied()
    {
        using var factory = new QueenZoneWebApplicationFactory();
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync($"{MobileAuthEndpoints.CallbackPath}?rid=missing");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("access_denied", payload.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DefaultHost_Authorize_ReportsProviderUnavailable()
    {
        using var factory = new QueenZoneWebApplicationFactory();
        var pair = MobileAuthPkceTestData.CreatePair();
        using var client = factory.CreateAnonymousClient(allowAutoRedirect: false);

        var response = await client.GetAsync(AuthorizeUrl("Google", pair.Challenge, "st"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
        Assert.Equal("temporarily_unavailable", query["error"].ToString());
    }

    private static string AuthorizeUrl(string provider, string challenge, string state) =>
        $"{MobileAuthEndpoints.AuthorizePath}?response_type=code" +
        $"&client_id={MobileAuthOptions.DefaultClientId}" +
        $"&redirect_uri={Uri.EscapeDataString(MobileAuthPkceTestData.RedirectUri)}" +
        $"&code_challenge={challenge}&code_challenge_method=S256" +
        $"&state={Uri.EscapeDataString(state)}&provider={provider}";
}

internal static class MobileAuthCookieAssertions
{
    public static void AssertDoesNotContainMemberCookie(this HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return;
        }

        Assert.DoesNotContain(
            cookies,
            cookie => cookie.Contains(AdminAuthenticationSchemes.MemberCookieName, StringComparison.OrdinalIgnoreCase)
                || cookie.StartsWith("MembersCookie=", StringComparison.OrdinalIgnoreCase));
    }
}
