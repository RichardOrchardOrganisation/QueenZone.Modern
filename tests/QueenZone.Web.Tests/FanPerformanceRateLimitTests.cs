using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class FanPerformanceRateLimitTests : IClassFixture<WebHostVariantCache>
{
    private readonly WebHostVariantCache variants;

    public FanPerformanceRateLimitTests(WebHostVariantCache variants)
    {
        this.variants = variants;
    }

    [Fact]
    public async Task AudioEndpoint_Returns429_AfterPermitLimitExceeded()
    {
        var factory = variants.Get(WebHostVariants.ExternalCookieFanPerformanceAudioLimit1);
        var client = await CreateSignedInMemberClientAsync(factory);

        var first = await client.GetAsync("/fan-performances/187/audio");
        var second = await client.GetAsync("/fan-performances/187/audio");

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task AudioEndpoint_AnonymousVisitor_StillRedirectsToLogin_WhenLimitExceeded()
    {
        var factory = variants.Get(WebHostVariants.ExternalCookieFanPerformanceAudioLimit1);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var first = await client.GetAsync("/fan-performances/187/audio");
        var second = await client.GetAsync("/fan-performances/187/audio");

        // Anonymous users are caught by auth before reaching the rate limiter,
        // so both requests redirect to login regardless of the permit count.
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Contains("/account/login", first.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Contains("/account/login", second.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task BrowsePage_Returns429_AfterPermitLimitExceeded()
    {
        var factory = variants.Get(WebHostVariants.ExternalCookieFanPerformanceBrowseLimit1);
        var client = factory.CreateClient();

        var first = await client.GetAsync("/fan-performances");
        var second = await client.GetAsync("/fan-performances");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task BrowsePage_AllowsRequestsWithinLimit()
    {
        var factory = variants.Get(WebHostVariants.ExternalCookieFanPerformanceBrowseLimit5);
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var response = await client.GetAsync("/fan-performances");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    private static async Task<HttpClient> CreateSignedInMemberClientAsync(
        WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true,
            AllowAutoRedirect = false,
        });
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.ProviderHeader, "Google");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.SubjectHeader, TestIds.For("google-rate-limit-test-subject"));
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.EmailHeader, $"{TestIds.For("ratelimit")}@example.com");
        client.DefaultRequestHeaders.Add(ExternalCookieTestHandler.NameHeader, "Rate Limit Test Member");

        var callbackResponse = await client.GetAsync("/account/external-login-callback");
        Assert.Equal(HttpStatusCode.Redirect, callbackResponse.StatusCode);
        Assert.DoesNotContain("/account/login", callbackResponse.Headers.Location!.OriginalString);

        return client;
    }
}
