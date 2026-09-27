using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Fail-closed Production host settings used by <see cref="ProductionWebApplicationFactory"/>
/// and named Production variants. Stubs Entra, blob, member OAuth, and an empty legacy
/// connection string so the host boots without Azure.
/// </summary>
public static class ProductionHostSettings
{
    internal const string BlobConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=test;AccountKey=dGVzdA==;EndpointSuffix=core.windows.net";

    internal const string MobileAuthSigningKey = "testing-mobile-auth-signing-key-32b!";

    private static readonly string DataProtectionKeysPath =
        Path.Combine(Path.GetTempPath(), "QueenZone.Web.Tests", "data-protection-keys");

    internal static readonly Dictionary<string, string?> Values = new()
    {
        ["QueenZoneHostFiltering:AllowedHosts"] = "localhost;127.0.0.1",
        ["DataProtection:KeysPath"] = DataProtectionKeysPath,
        ["ConnectionStrings:QueenZoneLegacy"] = string.Empty,
        ["ConnectionStrings:BlobStorage"] = BlobConnectionString,
        ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
        ["AzureAd:TenantId"] = "22222222-3333-4444-5555-666666666666",
        ["AzureAd:ClientId"] = "11111111-2222-3333-4444-555555555555",
        ["AzureAd:ClientSecret"] = "test-secret-not-used",
        ["AzureAd:CallbackPath"] = "/signin-oidc",
        ["Admin:AllowedEmails:0"] = "admin@test.local",
        ["Authentication:Google:ClientId"] = "test-google-client-id",
        ["Authentication:Google:ClientSecret"] = "test-google-client-secret",
        ["Analytics:MeasurementId"] = "G-V2W56BZ3KZ",
        ["MobileAuth:SigningKey"] = MobileAuthSigningKey,
    };

    public static void Apply(IWebHostBuilder builder)
    {
        foreach (var pair in Values)
        {
            builder.UseSetting(pair.Key, pair.Value);
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Values));
    }
}

/// <summary>
/// Class fixture for a Production-environment host with the fail-closed settings stubbed.
/// Use as <c>IClassFixture</c> so the host boots once per class; calling
/// <c>WithWebHostBuilder</c> in a test constructor boots a new host for every test.
/// Shared Production cases should take <see cref="ProductionHostFixture"/> instead.
/// </summary>
public class ProductionWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        ProductionHostSettings.Apply(builder);
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
public sealed class ExternalCookieWebApplicationFactory : VariantWebApplicationFactory
{
    public ExternalCookieWebApplicationFactory()
        : base(WebHostVariants.ExternalCookie)
    {
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
