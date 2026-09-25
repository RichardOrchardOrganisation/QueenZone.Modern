using Microsoft.AspNetCore.Authentication;

namespace QueenZone.Web;

public static class HttpContextMemberAuthExtensions
{
    /// <summary>
    /// Authenticates the current request against the member cookie, falling back to
    /// <see cref="TestMemberAuthHandler"/> on automated-test hosts (<see
    /// cref="QueenZoneEnvironments.UsesTestAuth"/>) when no member cookie is present, since
    /// WebApplicationFactory and Playwright cannot complete a real external-provider login.
    /// </summary>
    public static async Task<AuthenticateResult> AuthenticateMemberAsync(this HttpContext httpContext)
    {
        var memberCookie = await httpContext.AuthenticateAsync(MemberAuthenticationSchemes.MembersCookie);
        if (memberCookie.Succeeded)
        {
            return memberCookie;
        }

        var environment = httpContext.RequestServices.GetService<IHostEnvironment>();
        if (environment is not null && QueenZoneEnvironments.UsesTestAuth(environment))
        {
            var testMember = await httpContext.AuthenticateAsync(TestMemberAuthHandler.SchemeName);
            if (testMember.Succeeded)
            {
                return testMember;
            }
        }

        return memberCookie;
    }

    /// <summary>
    /// Returns the signed-in member's id from <see cref="AuthenticateMemberAsync"/>, or
    /// <see langword="null"/> when the request is not a member session. The ambient
    /// <c>HttpContext.User</c> is deliberately ignored because it may be the admin scheme.
    /// </summary>
    public static async Task<Guid?> AuthenticateMemberIdAsync(this HttpContext httpContext)
    {
        var authResult = await httpContext.AuthenticateMemberAsync();
        return authResult.Succeeded ? ForumMember.GetMemberId(authResult.Principal) : null;
    }

    /// <summary>
    /// Returns the member id from the ambient <c>HttpContext.User</c> when it already carries
    /// one, otherwise falls back to <see cref="AuthenticateMemberIdAsync"/>.
    /// </summary>
    public static async Task<Guid?> GetSignedInMemberIdAsync(this HttpContext httpContext) =>
        ForumMember.GetMemberId(httpContext.User) ?? await httpContext.AuthenticateMemberIdAsync();
}
