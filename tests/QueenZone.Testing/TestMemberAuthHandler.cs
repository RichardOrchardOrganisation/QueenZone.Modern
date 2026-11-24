using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace QueenZone.Web;

public sealed class TestMemberAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestMember";
    public const string MemberIdHeader = "X-Test-Member-Id";
    public const string DisplayNameHeader = "X-Test-Member-Name";
    public const string EmailHeader = "X-Test-Member-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(MemberIdHeader, out var idValues)
            || !Guid.TryParse(idValues.FirstOrDefault(), out var memberId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var displayName = Request.Headers.TryGetValue(DisplayNameHeader, out var nameValues)
            ? nameValues.FirstOrDefault()
            : null;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, memberId.ToString()),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(displayName) ? "Test Member" : displayName),
        };

        if (Request.Headers.TryGetValue(EmailHeader, out var emailValues)
            && !string.IsNullOrWhiteSpace(emailValues.FirstOrDefault()))
        {
            claims.Add(new Claim(ClaimTypes.Email, emailValues.FirstOrDefault()!));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var returnUrl = Request.PathBase + Request.Path + Request.QueryString;
        Response.Redirect($"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        return Task.CompletedTask;
    }
}
