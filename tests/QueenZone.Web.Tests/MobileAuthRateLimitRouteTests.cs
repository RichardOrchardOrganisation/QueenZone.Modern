using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Web.Tests;

public sealed class MobileAuthRateLimitRouteTests : IClassFixture<WebHostVariantCache>
{
    private readonly VariantWebApplicationFactory ipAuthorize;
    private readonly VariantWebApplicationFactory ipPassword;
    private readonly VariantWebApplicationFactory accountLimit;

    public MobileAuthRateLimitRouteTests(WebHostVariantCache variants)
    {
        ipAuthorize = variants.Get(WebHostVariants.ExternalCookieMobilePkceAuthRateLimitIp1);
        ipPassword = variants.Get(WebHostVariants.ExternalCookieMobilePkceAuthRateLimitIp1Password);
        accountLimit = variants.Get(WebHostVariants.ExternalCookieMobilePkceAuthRateLimitAccount1);
    }

    [Fact]
    public async Task Authorize_ReturnsRfc6749TooManyRequests_AfterIpLimit()
    {
        using var client = ipAuthorize.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var pair = MobileAuthPkceTestData.CreatePair();

        var first = await client.GetAsync(AuthorizeUrl(pair.Challenge));
        var second = await client.GetAsync(AuthorizeUrl(pair.Challenge));

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("application/json", second.Content.Headers.ContentType?.MediaType);
        var payload = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("temporarily_unavailable", payload.GetProperty("error").GetString());
        Assert.DoesNotContain("code_challenge", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_ReturnsRfc6749TooManyRequests_AfterAccountLimit()
    {
        var issued = await CompletePkceAsync(accountLimit);

        using var refreshRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = MobileAuthOptions.DefaultClientId,
            ["refresh_token"] = issued.RefreshToken,
        });
        var refresh = await issued.Client.PostAsync(MobileAuthEndpoints.TokenPath, refreshRequest);

        Assert.Equal(HttpStatusCode.TooManyRequests, refresh.StatusCode);
        var body = await refresh.Content.ReadAsStringAsync();
        Assert.Contains("temporarily_unavailable", body, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.RefreshToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(issued.AccessToken, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PasswordGrant_ReturnsRfc6749TooManyRequests_AfterIpLimit()
    {
        using (var scope = ipPassword.Services.CreateScope())
        {
            var members = scope.ServiceProvider.GetRequiredService<MemberAccountService>();
            var seeded = await members.RegisterAsync(
                "auth-password-rate@example.com",
                "correct horse battery staple",
                "Auth Rate Fan");
            Assert.True(seeded.Succeeded, seeded.Error);
        }

        using var client = ipPassword.CreateAnonymousClient();
        using var firstRequest = PasswordForm();
        var first = await client.PostAsync(MobileAuthEndpoints.TokenPath, firstRequest);
        using var secondRequest = PasswordForm();
        var second = await client.PostAsync(MobileAuthEndpoints.TokenPath, secondRequest);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("application/json", second.Content.Headers.ContentType?.MediaType);
        var payload = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("temporarily_unavailable", payload.GetProperty("error").GetString());
        Assert.DoesNotContain("auth-password-rate@example.com", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain("correct horse battery staple", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static async Task<(HttpClient Client, string AccessToken, string RefreshToken)> CompletePkceAsync(
        QueenZoneWebApplicationFactory factory)
    {
        var pair = MobileAuthPkceTestData.CreatePair();
        const string state = "rate-limit-state";
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, MemberAuthenticationSchemes.Google);
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-rate-limit-auth");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, "auth-rate@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Auth Rate Fan");

        var authorize = await client.GetAsync(AuthorizeUrl(pair.Challenge, state));
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
        var payload = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        return (
            client,
            payload.GetProperty("access_token").GetString()!,
            payload.GetProperty("refresh_token").GetString()!);
    }

    private static FormUrlEncodedContent PasswordForm() =>
        new(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = MobileAuthOptions.DefaultClientId,
            ["username"] = "auth-password-rate@example.com",
            ["password"] = "correct horse battery staple",
        });

    private static string AuthorizeUrl(string challenge, string state = "st") =>
        $"{MobileAuthEndpoints.AuthorizePath}?response_type=code" +
        $"&client_id={MobileAuthOptions.DefaultClientId}" +
        $"&redirect_uri={Uri.EscapeDataString(MobileAuthPkceTestData.RedirectUri)}" +
        $"&code_challenge={challenge}&code_challenge_method=S256" +
        $"&state={Uri.EscapeDataString(state)}&provider=Google";
}
