using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.Apple;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace QueenZone.Web.Tests;

public sealed class AppleAuthenticationSupportTests
{
    [Fact]
    public async Task AddQueenZoneAuth_registers_apple_with_generated_client_secret()
    {
        const string pem = "-----BEGIN PRIVATE KEY-----\\nabc\\n-----END PRIVATE KEY-----";
        using var provider = CreateAppleProvider(pem);

        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(MemberAuthenticationSchemes.Apple);
        var options = provider.GetRequiredService<IOptionsMonitor<AppleAuthenticationOptions>>()
            .Get(MemberAuthenticationSchemes.Apple);

        Assert.NotNull(scheme);
        Assert.Equal("org.queenzone.web", options.ClientId);
        Assert.Equal("TEAM123456", options.TeamId);
        Assert.Equal("KEY1234567", options.KeyId);
        Assert.True(options.GenerateClientSecret);
        Assert.NotNull(options.PrivateKey);
        var normalizedKey = await options.PrivateKey(options.KeyId!, CancellationToken.None);
        Assert.Equal(pem.Replace("\\n", "\n", StringComparison.Ordinal), normalizedKey.ToString());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Apple_ticket_reads_first_login_name_only_from_form_and_claims_identity(
        bool hasForm, bool hasIdentity)
    {
        using var provider = CreateAppleProvider("-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----");
        var scheme = await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetSchemeAsync(MemberAuthenticationSchemes.Apple);
        var options = provider.GetRequiredService<IOptionsMonitor<AppleAuthenticationOptions>>()
            .Get(MemberAuthenticationSchemes.Apple);
        var principal = hasIdentity ? new ClaimsPrincipal(new ClaimsIdentity("Apple")) : new ClaimsPrincipal();
        var httpContext = new DefaultHttpContext();
        if (hasForm)
        {
            httpContext.Request.ContentType = "application/x-www-form-urlencoded";
            httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
            {
                ["user"] = """{"name":{"firstName":"Freddie","lastName":"Mercury"}}""",
            });
        }

        using var backchannel = new HttpClient();
        using var tokenResponse = OAuthTokenResponse.Success(JsonDocument.Parse("{}"));
        using var user = JsonDocument.Parse("{}");
        var context = new OAuthCreatingTicketContext(
            principal, new AuthenticationProperties(), httpContext, scheme!, options,
            backchannel, tokenResponse, user.RootElement);
        await options.Events.OnCreatingTicket(context);

        Assert.Equal(hasForm && hasIdentity ? "Freddie Mercury" : null, principal.FindFirstValue(ClaimTypes.Name));
    }

    private static ServiceProvider CreateAppleProvider(string pem)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:ClientId"] = "",
                ["Authentication:Apple:ClientId"] = "org.queenzone.web",
                ["Authentication:Apple:TeamId"] = "TEAM123456",
                ["Authentication:Apple:KeyId"] = "KEY1234567",
                ["Authentication:Apple:PrivateKey"] = pem,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueenZoneAuth(configuration, new FakeHostEnvironment("Development"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void NormalizePrivateKey_converts_escaped_newlines_and_trims()
    {
        var normalized = AppleAuthenticationSupport.NormalizePrivateKey(
            "  -----BEGIN PRIVATE KEY-----\\nabc\\n-----END PRIVATE KEY-----  ");

        Assert.Equal(
            "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----",
            normalized.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void AddNameClaim_adds_apples_one_time_name()
    {
        var identity = new ClaimsIdentity();

        AppleAuthenticationSupport.AddNameClaim(
            identity,
            """{"name":{"firstName":" Freddie ","lastName":"Mercury"},"email":"relay@example.com"}""");

        Assert.Equal("Freddie Mercury", identity.FindFirst(ClaimTypes.Name)?.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{\"email\":\"relay@example.com\"}")]
    public void AddNameClaim_ignores_missing_or_invalid_optional_profile(string? userJson)
    {
        var identity = new ClaimsIdentity();

        AppleAuthenticationSupport.AddNameClaim(identity, userJson);

        Assert.Null(identity.FindFirst(ClaimTypes.Name));
    }

    [Fact]
    public void AddNameClaim_does_not_replace_an_existing_name()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "Existing Name")]);

        AppleAuthenticationSupport.AddNameClaim(
            identity,
            """{"name":{"firstName":"Apple","lastName":"Name"}}""");

        Assert.Equal("Existing Name", identity.FindFirst(ClaimTypes.Name)?.Value);
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "QueenZone.Web.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
