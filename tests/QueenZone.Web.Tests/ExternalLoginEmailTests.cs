using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using QueenZone.Web.Pages.Account;

namespace QueenZone.Web.Tests;

public sealed class ExternalLoginEmailTests
{
    [Theory]
    [InlineData(MemberAuthenticationSchemes.Google, ExternalLoginEmail.EmailVerifiedClaimType)]
    [InlineData(MemberAuthenticationSchemes.Microsoft, ExternalLoginEmail.EmailVerifiedClaimType)]
    [InlineData(MemberAuthenticationSchemes.Apple, ExternalLoginEmail.EmailVerifiedClaimType)]
    [InlineData(MemberAuthenticationSchemes.GitHub, ExternalLoginEmail.EmailVerifiedClaimType)]
    [InlineData(MemberAuthenticationSchemes.Discord, ExternalLoginEmail.DiscordVerifiedClaimType)]
    public void IsVerified_RequiresTheProviderSignal(string provider, string claimType)
    {
        var unverified = Principal(provider);
        var verified = Principal(provider, new Claim(claimType, "true"));
        var denied = Principal(provider, new Claim(claimType, "false"));

        Assert.False(ExternalLoginEmail.IsVerified(provider, unverified));
        Assert.True(ExternalLoginEmail.IsVerified(provider, verified));
        Assert.False(ExternalLoginEmail.IsVerified(provider, denied));
    }

    [Fact]
    public void IsVerified_IgnoresEmailVerifiedClaim_ForDiscord()
    {
        var principal = Principal(
            MemberAuthenticationSchemes.Discord,
            new Claim(ExternalLoginEmail.EmailVerifiedClaimType, "true"));

        Assert.False(ExternalLoginEmail.IsVerified(MemberAuthenticationSchemes.Discord, principal));
    }

    [Fact]
    public void ReadJsonBoolean_ReadsBooleansAndTrueFalseStrings()
    {
        using var document = JsonDocument.Parse("""
            {
              "email_verified": true,
              "verified": false,
              "text": "TRUE",
              "nope": 1
            }
            """);

        Assert.Equal("true", ExternalLoginEmail.ReadJsonBoolean(document.RootElement, "email_verified"));
        Assert.Equal("false", ExternalLoginEmail.ReadJsonBoolean(document.RootElement, "verified"));
        Assert.Equal("true", ExternalLoginEmail.ReadJsonBoolean(document.RootElement, "text"));
        Assert.Null(ExternalLoginEmail.ReadJsonBoolean(document.RootElement, "nope"));
        Assert.Null(ExternalLoginEmail.ReadJsonBoolean(document.RootElement, "missing"));
    }

    [Fact]
    public void ReadEmailVerifiedFromIdToken_ReadsThePayloadClaim()
    {
        var token = UnsignedJwt(new { email_verified = true, email = "fan@example.com" });

        Assert.Equal("true", ExternalLoginEmail.ReadEmailVerifiedFromIdToken(token));
        Assert.Null(ExternalLoginEmail.ReadEmailVerifiedFromIdToken("not-a-jwt"));
        Assert.Null(ExternalLoginEmail.ReadEmailVerifiedFromIdToken(null));
    }

    [Fact]
    public void ApplyIdTokenEmailVerified_DoesNotReplaceAnExistingClaim()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ExternalLoginEmail.EmailVerifiedClaimType, "false")],
            "Microsoft");

        ExternalLoginEmail.ApplyIdTokenEmailVerified(
            identity,
            UnsignedJwt(new { email_verified = true }));

        Assert.Equal("false", identity.FindFirst(ExternalLoginEmail.EmailVerifiedClaimType)?.Value);
    }

    [Fact]
    public async Task GitHubVerifiedEmail_UsesVerifiedPrimary_AndDropsTheProfileAddress()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "public-unverified@example.com")],
            MemberAuthenticationSchemes.GitHub);
        var handler = new StubHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            """
            [
              {"email":"first-unverified@example.com","primary":false,"verified":true},
              {"email":"primary-unverified@example.com","primary":true,"verified":false},
              {"email":"primary-verified@example.com","primary":true,"verified":true}
            ]
            """));

        await GitHubVerifiedEmail.ApplyAsync(
            identity,
            new HttpClient(handler),
            "token",
            "https://api.github.com/user/emails",
            "GitHub",
            CancellationToken.None);

        Assert.Equal("primary-verified@example.com", identity.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("true", identity.FindFirst(ExternalLoginEmail.EmailVerifiedClaimType)?.Value);
        Assert.Equal("https://api.github.com/user/emails", handler.LastRequestUri?.ToString());
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
    }

    [Fact]
    public async Task GitHubVerifiedEmail_ClearsProfileEmail_WhenNoVerifiedPrimaryExists()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Email, "public@example.com"),
                new Claim(ExternalLoginEmail.EmailVerifiedClaimType, "true"),
            ],
            MemberAuthenticationSchemes.GitHub);

        await GitHubVerifiedEmail.ApplyAsync(
            identity,
            new HttpClient(new StubHandler(_ => JsonResponse(
                HttpStatusCode.OK,
                """[{"email":"public@example.com","primary":true,"verified":false}]"""))),
            "token",
            "https://api.github.com/user/emails",
            "GitHub",
            CancellationToken.None);

        Assert.Null(identity.FindFirst(ClaimTypes.Email));
        Assert.Null(identity.FindFirst(ExternalLoginEmail.EmailVerifiedClaimType));
    }

    [Fact]
    public async Task GitHubVerifiedEmail_ClearsProfileEmail_WhenTheEmailsApiFails()
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "public@example.com")],
            MemberAuthenticationSchemes.GitHub);

        await GitHubVerifiedEmail.ApplyAsync(
            identity,
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))),
            "token",
            "https://api.github.com/user/emails",
            "GitHub",
            CancellationToken.None);

        Assert.Null(identity.FindFirst(ClaimTypes.Email));
        Assert.False(ExternalLoginEmail.IsVerified(MemberAuthenticationSchemes.GitHub, new ClaimsPrincipal(identity)));
    }

    [Fact]
    public async Task GitHubVerifiedEmail_ClearsProfileEmail_WhenTheTokenOrPayloadIsUnusable()
    {
        var missingToken = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "public@example.com")],
            MemberAuthenticationSchemes.GitHub);
        await GitHubVerifiedEmail.ApplyAsync(
            missingToken,
            new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            accessToken: null,
            "https://api.github.com/user/emails",
            "GitHub",
            CancellationToken.None);

        var notAnArray = new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "public@example.com")],
            MemberAuthenticationSchemes.GitHub);
        await GitHubVerifiedEmail.ApplyAsync(
            notAnArray,
            new HttpClient(new StubHandler(_ => JsonResponse(HttpStatusCode.OK, """{"email":"public@example.com"}"""))),
            "token",
            "https://api.github.com/user/emails",
            "GitHub",
            CancellationToken.None);

        var blankPrimary = new ClaimsIdentity(authenticationType: MemberAuthenticationSchemes.GitHub);
        await GitHubVerifiedEmail.ApplyAsync(
            blankPrimary,
            new HttpClient(new StubHandler(_ => JsonResponse(
                HttpStatusCode.OK,
                """[{"email":"  ","primary":true,"verified":true}]"""))),
            "token",
            "https://api.github.com/user/emails",
            claimsIssuer: null,
            CancellationToken.None);

        Assert.Null(missingToken.FindFirst(ClaimTypes.Email));
        Assert.Null(notAnArray.FindFirst(ClaimTypes.Email));
        Assert.Null(blankPrimary.FindFirst(ClaimTypes.Email));
    }

    [Fact]
    public void SelectConfirmationProviders_KeepsEnabledLinkedProviders()
    {
        var providers = LinkExternalLoginModel.SelectConfirmationProviders(
            ["Google", "GitHub", "Discord"],
            provider => provider is MemberAuthenticationSchemes.Google or MemberAuthenticationSchemes.Discord);

        Assert.Equal(["Google", "Discord"], providers);
    }

    private static ClaimsPrincipal Principal(string provider, params Claim[] extra)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "subject"),
            new(ClaimTypes.Email, "fan@example.com"),
        };
        claims.AddRange(extra);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, provider));
    }

    private static string UnsignedJwt(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return Base64Url("""{"alg":"none"}""") + "." + Base64Url(json) + ".sig";
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(responder(request));
        }
    }
}
