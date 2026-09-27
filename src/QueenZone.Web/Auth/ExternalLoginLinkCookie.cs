using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using QueenZone.Web.Infrastructure;

namespace QueenZone.Web;

/// <summary>
/// Short-lived holder for a new provider subject that matches an existing account.
/// The external sign-in cookie is cleared so a later provider challenge can use it,
/// while this cookie keeps the link that still needs confirmation.
/// </summary>
internal static class ExternalLoginLinkCookie
{
    public const string CookieName = ".QueenZone.MembersExternalLink";

    public const string PagePath = "/account/link-external-login";

    public const string ProviderClaimType = "provider";

    public const string ReturnUrlClaimType = "return_url";

    public const string MobileRequestIdClaimType = "mobile_request_id";

    public const string ProtectedAppleRefreshTokenClaimType = "apple_refresh_token";

    public static async Task SignInAsync(HttpContext httpContext, PendingExternalLink link)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(link);

        var claims = new List<Claim>
        {
            new(ProviderClaimType, link.Provider),
            new(ClaimTypes.NameIdentifier, link.ProviderKey),
            new(ClaimTypes.Email, link.Email),
            new(ClaimTypes.Name, link.DisplayName),
            new(ReturnUrlClaimType, link.ReturnUrl),
        };
        if (!string.IsNullOrWhiteSpace(link.MobileRequestId))
        {
            claims.Add(new Claim(MobileRequestIdClaimType, link.MobileRequestId));
        }
        if (!string.IsNullOrWhiteSpace(link.ProtectedAppleRefreshToken))
        {
            claims.Add(new Claim(ProtectedAppleRefreshTokenClaimType, link.ProtectedAppleRefreshToken));
        }

        var identity = new ClaimsIdentity(claims, MemberAuthenticationSchemes.ExternalLinkCookie);
        await httpContext.SignInAsync(
            MemberAuthenticationSchemes.ExternalLinkCookie,
            new ClaimsPrincipal(identity));
    }

    public static async Task<PendingExternalLink?> ReadAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var result = await httpContext.AuthenticateAsync(MemberAuthenticationSchemes.ExternalLinkCookie);
        if (!result.Succeeded || result.Principal is null)
        {
            return null;
        }

        var provider = result.Principal.FindFirstValue(ProviderClaimType);
        var providerKey = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = result.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(provider)
            || string.IsNullOrWhiteSpace(providerKey)
            || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var displayName = result.Principal.FindFirstValue(ClaimTypes.Name) ?? email;
        var returnUrl = LocalReturnUrl.Resolve(result.Principal.FindFirstValue(ReturnUrlClaimType));
        var mobileRequestId = result.Principal.FindFirstValue(MobileRequestIdClaimType);
        var protectedAppleRefreshToken = result.Principal.FindFirstValue(ProtectedAppleRefreshTokenClaimType);
        return new PendingExternalLink(
            provider,
            providerKey,
            email,
            displayName,
            returnUrl,
            string.IsNullOrWhiteSpace(mobileRequestId) ? null : mobileRequestId,
            protectedAppleRefreshToken);
    }

    public static Task SignOutAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.SignOutAsync(MemberAuthenticationSchemes.ExternalLinkCookie);
    }
}

internal sealed record PendingExternalLink(
    string Provider,
    string ProviderKey,
    string Email,
    string DisplayName,
    string ReturnUrl,
    string? MobileRequestId,
    string? ProtectedAppleRefreshToken = null);
