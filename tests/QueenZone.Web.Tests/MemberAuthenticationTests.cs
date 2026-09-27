using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class MemberAuthenticationTests :
    IClassFixture<QueenZoneWebApplicationFactory>,
    IClassFixture<WebHostVariantCache>
{
    private readonly QueenZoneWebApplicationFactory factory;
    private readonly WebHostVariantCache variants;

    public MemberAuthenticationTests(
        QueenZoneWebApplicationFactory factory,
        WebHostVariantCache variants)
    {
        this.factory = factory;
        this.variants = variants;
    }

    [Fact]
    public async Task AnonymousUserCannotAccessMemberProbe()
    {
        // Cookie auth challenges with a 302 redirect to the login page rather than a bare 401,
        // so don't auto-follow the redirect — assert the challenge itself.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/account/member-probe");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task AdminTestHeaderAloneDoesNotGrantMemberAccess()
    {
        // The Admin allowlist scheme ("Test") and the Member policy's scheme ("MembersCookie")
        // are deliberately separate auth schemes, so being an authenticated admin user does not
        // implicitly grant member access.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, "admin@test.local");

        var response = await client.GetAsync("/account/member-probe");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/account/login", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task LoginPageRenders()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/account/login");

        Assert.Contains("Sign in", body);
        Assert.Contains("Sign in to QueenZone", body);
        Assert.DoesNotContain("Continue with Apple", body);
    }

    [Fact]
    public async Task LoginPageShowsAppleOnlyWhenFullyConfigured()
    {
        var configuredFactory = variants.Get(WebHostVariants.TestingAppleOAuth);
        var client = configuredFactory.CreateClient();

        var body = await client.GetStringAsync("/account/login");

        Assert.Contains("Continue with Apple", body);
        Assert.Contains("provider=Apple", body);
    }

    [Fact]
    public async Task AppleLoginStartsAppleAuthorizationFlow()
    {
        var configuredFactory = variants.Get(WebHostVariants.TestingAppleOAuth);
        var client = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/account/external-login?provider=Apple&returnUrl=%2Fforum");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("appleid.apple.com", response.Headers.Location?.Host);
        Assert.Contains("response_mode=form_post", response.Headers.Location?.Query);
        Assert.DoesNotContain("prompt=", response.Headers.Location?.Query);
    }

    [Fact]
    public async Task AnonymousHeaderRendersMobileLoginAction()
    {
        var client = factory.CreateClient();

        var body = await client.GetStringAsync("/");

        Assert.Contains("href=\"/account/login\"", body);
        Assert.Contains(">Member sign in<", body);
    }

    [Fact]
    public async Task AdminHeaderDistinguishesAdminAccessFromMemberSignIn()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserEmailHeader, "admin@test.local");

        var body = await client.GetStringAsync("/admin");

        Assert.Contains("Admin access: admin@test.local", body);
        Assert.Contains(">Member sign in<", body);
        Assert.DoesNotContain("Signed in as admin", body);
    }

}
