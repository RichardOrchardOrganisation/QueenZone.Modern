using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class PublicHtmlCacheControlUnitTests
{
    [Fact]
    public void TryApply_sets_short_public_header_for_anonymous_html()
    {
        var context = CreateContext("/", authenticated: false, statusCode: 200, contentType: "text/html; charset=utf-8");

        Assert.True(PublicHtmlCacheControl.TryApply(context));
        Assert.Equal(PublicHtmlCacheControl.HeaderValue, context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void TryApply_skips_when_cache_control_already_set()
    {
        var context = CreateContext("/", authenticated: false, statusCode: 200, contentType: "text/html");
        context.Response.Headers.CacheControl = "public, max-age=86400";

        Assert.False(PublicHtmlCacheControl.TryApply(context));
        Assert.Equal("public, max-age=86400", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public void TryApply_skips_authenticated_and_admin_and_non_html()
    {
        var auth = CreateContext("/", authenticated: true, statusCode: 200, contentType: "text/html");
        Assert.False(PublicHtmlCacheControl.TryApply(auth));

        var admin = CreateContext("/admin/news", authenticated: false, statusCode: 200, contentType: "text/html");
        Assert.False(PublicHtmlCacheControl.TryApply(admin));

        var json = CreateContext("/health", authenticated: false, statusCode: 200, contentType: "application/json");
        Assert.False(PublicHtmlCacheControl.TryApply(json));

        var notFound = CreateContext("/", authenticated: false, statusCode: 404, contentType: "text/html");
        Assert.False(PublicHtmlCacheControl.TryApply(notFound));
    }

    private static DefaultHttpContext CreateContext(
        string path,
        bool authenticated,
        int statusCode,
        string contentType)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment("Production"));
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Request.Method = HttpMethods.Get;
        http.Request.Path = path;
        http.Response.StatusCode = statusCode;
        http.Response.ContentType = contentType;
        if (authenticated)
        {
            http.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "user@test.local")],
                    authenticationType: "Test"));
        }

        return http;
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "QueenZone.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

public sealed class PublicHtmlCacheControlIntegrationTests : IClassFixture<QueenZoneWebApplicationFactory>
{
    private readonly WebApplicationFactory<Program> factory;

    public PublicHtmlCacheControlIntegrationTests(QueenZoneWebApplicationFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task AnonymousPublicHtml_HasShortCacheControl()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(PublicHtmlCacheControl.HeaderValue, response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task AccountLogin_DoesNotUsePublicHtmlCacheControl()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        var response = await client.GetAsync("/account/login");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(PublicHtmlCacheControl.HeaderValue, response.Headers.CacheControl?.ToString());
    }
}
