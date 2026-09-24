namespace QueenZone.Web.Tests;

public sealed class SecurityHeadersTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly QueenZoneWebApplicationFactory factory;

    public SecurityHeadersTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/news")]
    [InlineData("/account/login")]
    public async Task Response_HasXContentTypeOptionsNosniff(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/news")]
    [InlineData("/account/login")]
    public async Task Response_HasXFrameOptionsDeny(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/news")]
    [InlineData("/account/login")]
    public async Task Response_HasReferrerPolicyStrictOriginWhenCrossOrigin(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").First());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/news")]
    [InlineData("/account/login")]
    public async Task Response_HasPermissionsPolicy(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        Assert.Equal(
            SecurityHeaders.PermissionsPolicy,
            response.Headers.GetValues("Permissions-Policy").First());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/news")]
    [InlineData("/account/login")]
    [InlineData("/contact")]
    public async Task Response_HasEnforcingContentSecurityPolicy(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);
        Assert.False(response.Headers.Contains("Content-Security-Policy-Report-Only"));
        var csp = response.Headers.GetValues("Content-Security-Policy").First();
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.Contains("style-src 'self'; ", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("style-src 'self' 'unsafe-inline'", csp, StringComparison.Ordinal);
        Assert.Contains("connect-src 'self' ", csp, StringComparison.Ordinal);
        Assert.Contains("https://www.google.com;", csp, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ContentSecurityPolicy_ScriptSrc_UsesPerRequestNonce()
    {
        var client = factory.CreateClient();
        var firstCsp = (await client.GetAsync("/")).Headers.GetValues("Content-Security-Policy").First();
        var secondCsp = (await client.GetAsync("/")).Headers.GetValues("Content-Security-Policy").First();

        var firstNonce = ExtractNonce(firstCsp);
        var secondNonce = ExtractNonce(secondCsp);

        Assert.NotEqual(firstNonce, secondNonce);
        Assert.Equal(SecurityHeaders.BuildContentSecurityPolicy(firstNonce), firstCsp);
    }

    [Fact]
    public async Task Response_TimelineInlineScript_CarriesMatchingNonce()
    {
        var response = await factory.CreateClient().GetAsync("/timeline");
        var csp = response.Headers.GetValues("Content-Security-Policy").First();
        var nonce = ExtractNonce(csp);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Contains($"nonce=\"{nonce}\"", html, StringComparison.Ordinal);
    }

    private static string ExtractNonce(string csp)
    {
        const string marker = "'nonce-";
        var start = csp.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = csp.IndexOf('\'', start);
        return csp[start..end];
    }
}
