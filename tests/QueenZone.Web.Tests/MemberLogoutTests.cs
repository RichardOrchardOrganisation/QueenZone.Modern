using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace QueenZone.Web.Tests;

public sealed partial class MemberLogoutTests : IClassFixture<ExternalCookieWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public MemberLogoutTests(ExternalCookieWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task LogoutPost_ClearsMemberCookie_AndRedirectsToSignedOutLogin()
    {
        var client = await CreateSignedInMemberClientAsync(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var performancesPage = await client.GetStringAsync("/fan-performances");
        var token = ExtractAntiforgeryToken(performancesPage);
        var logoutResponse = await client.PostAsync(
            "/account/logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
            }));

        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal("/account/login?signedOut=1", logoutResponse.Headers.Location!.OriginalString);

        var probeResponse = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.Redirect, probeResponse.StatusCode);
        Assert.Contains("/account/login", probeResponse.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task LogoutGet_StillClearsMemberCookie_ForLegacyLinks()
    {
        var client = await CreateSignedInMemberClientAsync(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var logoutResponse = await client.GetAsync("/account/logout");

        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal("/account/login?signedOut=1", logoutResponse.Headers.Location!.OriginalString);

        var probeResponse = await client.GetAsync("/account/member-probe");
        Assert.Equal(HttpStatusCode.Redirect, probeResponse.StatusCode);
    }

    [Fact]
    public async Task SignedOutLoginPage_ShowsConfirmationMessage()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/account/login?signedOut=1");

        Assert.Contains("You have been signed out of QueenZone.", body);
    }

    [Fact]
    public async Task SignedInHeader_RendersPostSignOutForm()
    {
        var client = await CreateSignedInMemberClientAsync();

        var body = await client.GetStringAsync("/fan-performances");

        Assert.Contains("method=\"post\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("action=\"/account/logout\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(">Sign out</button>", body);
        Assert.DoesNotContain("href=\"/account/logout\"", body);
    }

    private async Task<HttpClient> CreateSignedInMemberClientAsync(
        WebApplicationFactoryClientOptions? options = null)
    {
        var client = factory.CreateClient(options ?? new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = true,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, "google-logout-subject");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, "logouttest@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Logout Test");

        var callbackResponse = await client.GetAsync("/account/external-login-callback");
        Assert.True(
            callbackResponse.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect,
            $"Unexpected callback status code: {callbackResponse.StatusCode}");
        if (callbackResponse.StatusCode == HttpStatusCode.Redirect)
        {
            Assert.DoesNotContain("/account/login", callbackResponse.Headers.Location!.OriginalString);
        }

        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenRegex().Match(html);
        Assert.True(match.Success, "Antiforgery token was not found in the form.");
        return match.Groups["token"].Value;
    }

    [GeneratedRegex("""name="__RequestVerificationToken" value="(?<token>[^"]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryTokenRegex();
}
