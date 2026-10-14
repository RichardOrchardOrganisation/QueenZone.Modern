using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace QueenZone.Web;

/// <summary>
/// Forum moderation uses the same Entra (or test) admin scheme as <c>/Admin</c>.
/// A member cookie is never admin, even when its email is on <c>Admin:AllowedEmails</c>.
/// </summary>
public static class ForumAdminAccess
{
    public static async Task<bool> IsAdminAsync(HttpContext httpContext, AdminOptions adminOptions)
    {
        var adminAuth = await httpContext.AuthenticateAsync(AdminAuthenticationSchemes.CompositeScheme);
        if (!adminAuth.Succeeded)
        {
            return false;
        }

        var authz = httpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        return (await authz.AuthorizeAsync(adminAuth.Principal!, AdminAuthenticationSchemes.Policy)).Succeeded;
    }
}
