using Microsoft.AspNetCore.Authentication;

namespace QueenZone.Web;

public static class MemberCookieFallbackMiddlewareExtensions
{
    public static IApplicationBuilder UseMemberCookieFallback(this IApplicationBuilder app)
    {
        // Public pages use a non-member default scheme; without this, HttpContext.User stays
        // anonymous while the MembersCookie is present. Antiforgery tokens then fail on member-only
        // APIs (e.g. editor image upload) because generation and validation see different identities.
        return app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var member = await context.AuthenticateAsync(MemberAuthenticationSchemes.MembersCookie);
                if (member.Succeeded && member.Principal?.Identity?.IsAuthenticated == true)
                {
                    context.User = member.Principal;
                }
            }

            await next();
        });
    }
}
