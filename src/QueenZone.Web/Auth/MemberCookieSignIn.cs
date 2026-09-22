using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using QueenZone.Data.Entities;

namespace QueenZone.Web;

internal static class MemberCookieSignIn
{
    public static Task SignInAsync(HttpContext httpContext, MemberAccount account)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(account);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Email, account.Email),
            new Claim(ClaimTypes.Name, account.DisplayName),
            MemberSessionGate.CreateIssuedAtClaim(DateTimeOffset.UtcNow),
        };
        var identity = new ClaimsIdentity(claims, MemberAuthenticationSchemes.MembersCookie);
        return httpContext.SignInAsync(
            MemberAuthenticationSchemes.MembersCookie,
            new ClaimsPrincipal(identity));
    }
}
