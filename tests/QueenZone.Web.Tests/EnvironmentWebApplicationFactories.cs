using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Class fixture for a Production-environment host with the fail-closed settings stubbed.
/// Use as <c>IClassFixture</c> so the host boots once per class; calling
/// <c>WithWebHostBuilder</c> in a test constructor boots a new host for every test.
/// </summary>
public sealed class ProductionWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        ResponseCompressionTests.ApplyProductionHostTestSettings(builder);
    }
}

/// <summary>
/// Testing host with <c>Site:PublicBaseUrl</c> set, for canonical-URL, SEO, and feed assertions.
/// </summary>
public sealed class PreviewPublicBaseUrlWebApplicationFactory : QueenZoneWebApplicationFactory
{
    public const string PublicBaseUrl = "https://preview.queenzone.test";

    protected override void ConfigureTestServices(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Site:PublicBaseUrl"] = PublicBaseUrl,
            });
        });
    }
}

/// <summary>
/// Testing host that also registers the member external-cookie scheme with
/// <see cref="ExternalCookieTestHandler"/>, for routes that challenge or read it.
/// </summary>
public sealed class ExternalCookieWebApplicationFactory : QueenZoneWebApplicationFactory
{
    protected override void ConfigureTestServices(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, ExternalCookieTestHandler>(
                    MemberAuthenticationSchemes.ExternalCookie, _ => { });
        });
    }
}

/// <summary>
/// Class fixture for a Development-environment host that never reads Local.json.
/// </summary>
public sealed class DevelopmentWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // testhost already skips Local.json; pin the flag so a runner that is not named
        // testhost still cannot inherit a half-configured Analytics pair.
        builder.UseSetting(QueenZoneDevelopmentHost.SkipLocalSettingsKey, "true");
    }
}
