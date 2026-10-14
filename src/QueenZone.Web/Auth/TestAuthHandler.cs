using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string UserEmailHeader = "X-Test-User-Email";
    public const string UserTenantHeader = "X-Test-User-Tenant";
    public const string UserOidHeader = "X-Test-User-Oid";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserEmailHeader, out var headerValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var email = headerValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Email, email),
            new Claim("preferred_username", email)
        };

        if (Request.Headers[UserTenantHeader].FirstOrDefault() is { } tenant)
        {
            claims.Add(new Claim("tid", tenant));
        }

        if (Request.Headers[UserOidHeader].FirstOrDefault() is { } oid)
        {
            claims.Add(new Claim("oid", oid));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
