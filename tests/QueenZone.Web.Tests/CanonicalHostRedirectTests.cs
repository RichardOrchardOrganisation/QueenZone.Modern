using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QueenZone.Web.Tests;

public sealed class CanonicalHostRedirectTests
{
    [Fact]
    public void TryBuildRedirectUrl_redirects_apex_host_to_configured_www_host()
    {
        var request = CreateRequest("queenzone.org", "/news", "?utm_source=search-console");

        var shouldRedirect = CanonicalHostRedirect.TryBuildRedirectUrl(
            request,
            "https://www.queenzone.org",
            out var redirectUrl);

        Assert.True(shouldRedirect);
        Assert.Equal("https://www.queenzone.org/news?utm_source=search-console", redirectUrl);
    }

    [Fact]
    public void TryBuildRedirectUrl_does_not_redirect_canonical_host()
    {
        var request = CreateRequest("www.queenzone.org", "/news", string.Empty);

        var shouldRedirect = CanonicalHostRedirect.TryBuildRedirectUrl(
            request,
            "https://www.queenzone.org",
            out var redirectUrl);

        Assert.False(shouldRedirect);
        Assert.Empty(redirectUrl);
    }

    [Fact]
    public void TryBuildRedirectUrl_does_not_redirect_when_public_host_is_not_www()
    {
        var request = CreateRequest("queenzone.test", "/", string.Empty);

        var shouldRedirect = CanonicalHostRedirect.TryBuildRedirectUrl(
            request,
            "https://preview.queenzone.test",
            out var redirectUrl);

        Assert.False(shouldRedirect);
        Assert.Empty(redirectUrl);
    }

    [Fact]
    public void TryBuildRedirectUrl_keeps_untrusted_path_and_query_on_canonical_origin()
    {
        var request = CreateRequest(
            "queenzone.org",
            "//evil.example/phish",
            "?next=https://evil.example/login");

        var shouldRedirect = CanonicalHostRedirect.TryBuildRedirectUrl(
            request,
            "https://www.queenzone.org",
            out var redirectUrl);

        Assert.True(shouldRedirect);
        var redirectUri = new Uri(redirectUrl);
        Assert.Equal(Uri.UriSchemeHttps, redirectUri.Scheme);
        Assert.Equal("www.queenzone.org", redirectUri.Host);
        Assert.Equal("?next=https://evil.example/login", redirectUri.Query);
    }

    private static HttpRequest CreateRequest(string host, string path, string queryString)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.QueryString = QueryString.FromUriComponent(queryString);
        return context.Request;
    }
}

public sealed class CanonicalHostRedirectIntegrationTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public CanonicalHostRedirectIntegrationTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Middleware_redirects_apex_request_to_validated_canonical_origin()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/news?source=apex");
        request.Headers.Host = "queenzone.org";

        using var response = await client.SendAsync(request);

        Assert.Equal(StatusCodes.Status301MovedPermanently, (int)response.StatusCode);
        Assert.Equal("https://www.queenzone.org/news?source=apex", response.Headers.Location?.AbsoluteUri);
    }
}
