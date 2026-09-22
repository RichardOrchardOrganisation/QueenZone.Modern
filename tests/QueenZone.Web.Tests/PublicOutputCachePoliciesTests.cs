using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class PublicOutputCachePoliciesTests
{
    [Fact]
    public void IsPublicReadOnlyRequest_rejects_authenticated_and_admin_paths()
    {
        var anonymousHome = CreateContext("/", authenticated: false, "Production");
        Assert.True(PublicOutputCachePolicies.IsPublicReadOnlyRequest(anonymousHome));

        var admin = CreateContext("/admin/news", authenticated: false, "Production");
        Assert.False(PublicOutputCachePolicies.IsPublicReadOnlyRequest(admin));

        var account = CreateContext("/account/login", authenticated: false, "Production");
        Assert.False(PublicOutputCachePolicies.IsPublicReadOnlyRequest(account));

        var authHome = CreateContext("/", authenticated: true, "Production");
        Assert.False(PublicOutputCachePolicies.IsPublicReadOnlyRequest(authHome));
    }

    [Fact]
    public void IsCacheablePublicHtmlRequest_is_disabled_in_testing()
    {
        var testing = CreateContext("/", authenticated: false, "Testing");
        Assert.False(PublicOutputCachePolicies.IsCacheablePublicHtmlRequest(testing));

        var production = CreateContext("/news", authenticated: false, "Production");
        Assert.True(PublicOutputCachePolicies.IsCacheablePublicHtmlRequest(production));

        var search = CreateContext("/search", authenticated: false, "Production");
        Assert.False(PublicOutputCachePolicies.IsCacheablePublicHtmlRequest(search));
    }

    [Theory]
    [InlineData("/api/uploads/editor-image")]
    [InlineData("/ugc/forum/x.webp")]
    [InlineData("/submit/news")]
    [InlineData("/help")]
    [InlineData("/contact")]
    [InlineData("/error")]
    [InlineData("/trivia")]
    [InlineData("/search")]
    [InlineData("/search/results")]
    public void IsPublicReadOnlyRequest_excludes_non_html_surfaces(string path)
    {
        var context = CreateContext(path, authenticated: false, "Production");
        Assert.False(PublicOutputCachePolicies.IsPublicReadOnlyRequest(context));
    }

    [Fact]
    public void PublicHtmlQueryKeys_include_functional_values_but_not_tracking_parameters()
    {
        Assert.Contains("page", PublicOutputCachePolicies.PublicHtmlQueryKeys);
        Assert.Contains("pageNumber", PublicOutputCachePolicies.PublicHtmlQueryKeys);
        Assert.Contains("size", PublicOutputCachePolicies.PublicHtmlQueryKeys);
        Assert.DoesNotContain("utm_source", PublicOutputCachePolicies.PublicHtmlQueryKeys);
        Assert.DoesNotContain("*", PublicOutputCachePolicies.PublicHtmlQueryKeys);
    }

    private static DefaultHttpContext CreateContext(string path, bool authenticated, string environmentName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment(environmentName));
        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        http.Request.Method = HttpMethods.Get;
        http.Request.Path = path;
        if (authenticated)
        {
            http.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "user@test.local")],
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
