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

        return httpContext.SignInAsync(
            MemberAuthenticationSchemes.MembersCookie,
            CreatePrincipal(account));
    }

    private static ClaimsPrincipal CreatePrincipal(MemberAccount account)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Email, account.Email),
            new Claim(ClaimTypes.Name, account.DisplayName),
            MemberThemeClaim.Create(account.ThemePreference),
            MemberSessionGate.CreateIssuedAtClaim(DateTimeOffset.UtcNow),
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, MemberAuthenticationSchemes.MembersCookie));
    }
}
