using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QueenZone.Web.Tests;

public sealed class AdminPrincipalRequirementTests : IClassFixture<PrincipalPinnedWebApplicationFactory>
{
    internal const string WorkTenant = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    internal const string ConsumerTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";
    internal const string ObjectId = "11111111-2222-3333-4444-555555555555";
    private readonly PrincipalPinnedWebApplicationFactory factory;

    public AdminPrincipalRequirementTests(PrincipalPinnedWebApplicationFactory factory) => this.factory = factory;

    [Theory]
    [InlineData(WorkTenant, ObjectId, true)]
    [InlineData(ConsumerTenant, ObjectId, true)]
    [InlineData(" AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE ", ObjectId, true)]
    [InlineData(WorkTenant, "wrong-object", false)]
    [InlineData("wrong-tenant", ObjectId, false)]
    [InlineData(null, ObjectId, false)]
    [InlineData(WorkTenant, null, false)]
    [InlineData(" ", ObjectId, false)]
    [InlineData(WorkTenant, " ", false)]
    public async Task Handler_matches_both_identifiers_and_warns_without_email_on_denial(
        string? tenant, string? oid, bool expected)
    {
        var logger = new PinLogger();
        var result = await EvaluateAsync(Principal(tenant, oid), true, Pins, logger);

        Assert.Equal(expected, result.HasSucceeded);
        Assert.Equal(!expected, result.HasFailed);
        if (expected)
        {
            Assert.Empty(logger.Entries);
        }
        else
        {
            var entry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.DoesNotContain(AdminHttpTestHelpers.AdminEmail, entry.Message);
            if (tenant is not null)
            {
                Assert.Contains(tenant.Trim(), entry.Message);
            }
            if (oid is not null)
            {
                Assert.Contains(oid.Trim(), entry.Message);
            }
        }
    }

    [Fact]
    public async Task Handler_accepts_long_claim_types()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("http://schemas.microsoft.com/identity/claims/tenantid", WorkTenant),
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", ObjectId)
        ], "admin"));

        Assert.True((await EvaluateAsync(principal, true, Pins, new PinLogger())).HasSucceeded);
    }

    [Fact]
    public async Task Enforcement_off_passes_without_claims_or_pins()
    {
        var logger = new PinLogger();
        Assert.True((await EvaluateAsync(new ClaimsPrincipal(), false, [], logger)).HasSucceeded);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Enforcement_on_with_empty_list_denies()
    {
        Assert.True((await EvaluateAsync(Principal(WorkTenant, ObjectId), true, [], new PinLogger())).HasFailed);
    }

    [Theory]
    [InlineData(WorkTenant, ObjectId, AdminHttpTestHelpers.AdminEmail, HttpStatusCode.OK)]
    [InlineData(ConsumerTenant, ObjectId, AdminHttpTestHelpers.AdminEmail, HttpStatusCode.OK)]
    [InlineData(WorkTenant, "wrong-object", AdminHttpTestHelpers.AdminEmail, HttpStatusCode.Forbidden)]
    [InlineData("wrong-tenant", ObjectId, AdminHttpTestHelpers.AdminEmail, HttpStatusCode.Forbidden)]
    [InlineData(WorkTenant, ObjectId, "outsider@test.local", HttpStatusCode.Forbidden)]
    public async Task Admin_route_requires_email_and_principal_pin(
        string tenant, string oid, string email, HttpStatusCode expected)
    {
        using var client = AdminHttpTestHelpers.CreateClient(factory, email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserTenantHeader, tenant);
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserOidHeader, oid);

        using var response = await client.GetAsync("/admin/news");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Matching_member_cookie_cannot_authenticate_admin_route()
    {
        using var client = AdminHttpTestHelpers.CreateClient(factory);
        var scheme = MemberAuthenticationSchemes.MembersCookie;
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(scheme);
        var ticket = new AuthenticationTicket(Principal(WorkTenant, ObjectId), new AuthenticationProperties(), scheme);
        var cookie = options.TicketDataFormat.Protect(ticket);
        client.DefaultRequestHeaders.Add("Cookie", $"{options.Cookie.Name}={cookie}");

        using var response = await client.GetAsync("/admin/news");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("tenant")]
    [InlineData(":object")]
    [InlineData("tenant: ")]
    [InlineData(" :object")]
    [InlineData("tenant:object:extra")]
    public void Validator_rejects_malformed_principals(string? entry)
    {
        var validator = new AdminOptionsValidator(new FakeHostEnvironment("Production"));
        var result = validator.Validate(null, new AdminOptions
        {
            AllowedEmails = [AdminHttpTestHelpers.AdminEmail],
            AllowedPrincipals = [entry!]
        });

        Assert.True(result.Failed);
        Assert.Contains("AllowedPrincipals", result.FailureMessage);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Validator_allows_empty_principal_list_in_production(bool enforce)
    {
        var validator = new AdminOptionsValidator(new FakeHostEnvironment("Production"));
        Assert.True(validator.Validate(null, new AdminOptions
        {
            AllowedEmails = [AdminHttpTestHelpers.AdminEmail],
            EnforcePrincipalPin = enforce
        }).Succeeded);
    }

    internal static string[] Pins => [$" {WorkTenant.ToUpperInvariant()} : {ObjectId} ", $"{ConsumerTenant}:{ObjectId}"];

    private static ClaimsPrincipal Principal(string? tenant, string? oid)
    {
        var claims = new List<Claim> { new(ClaimTypes.Email, AdminHttpTestHelpers.AdminEmail) };
        if (tenant is not null)
        {
            claims.Add(new Claim("tid", tenant));
        }
        if (oid is not null)
        {
            claims.Add(new Claim("oid", oid));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "admin"));
    }

    private static async Task<AuthorizationHandlerContext> EvaluateAsync(
        ClaimsPrincipal principal, bool enforce, string[] pins, PinLogger logger)
    {
        var handler = new AdminPrincipalRequirementHandler(new PinOptionsMonitor(new AdminOptions
        {
            EnforcePrincipalPin = enforce,
            AllowedPrincipals = pins
        }), logger);
        var context = new AuthorizationHandlerContext([new AdminPrincipalRequirement()], principal, null);
        await handler.HandleAsync(context);
        return context;
    }

    private sealed class PinOptionsMonitor(AdminOptions options) : IOptionsMonitor<AdminOptions>
    {
        public AdminOptions CurrentValue => options;
        public AdminOptions Get(string? name) => options;
        public IDisposable? OnChange(Action<AdminOptions, string?> listener) => null;
    }

    private sealed class PinLogger : ILogger<AdminPrincipalRequirementHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
    }
}

public sealed class PrincipalPinnedWebApplicationFactory : QueenZoneWebApplicationFactory
{
    protected override void ConfigureTestServices(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Admin:EnforcePrincipalPin"] = "true",
            ["Admin:AllowedPrincipals:0"] = AdminPrincipalRequirementTests.Pins[0],
            ["Admin:AllowedPrincipals:1"] = AdminPrincipalRequirementTests.Pins[1],
            ["Admin:AllowedEmails:0"] = AdminHttpTestHelpers.AdminEmail
        }));
    }
}
