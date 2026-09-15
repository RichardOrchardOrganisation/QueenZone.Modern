using System.Diagnostics;
using Microsoft.Extensions.Options;
using QueenZone.Web.Health;

namespace QueenZone.Web;

public sealed class RequestLogScopeMiddleware(
    RequestDelegate next,
    ILogger<RequestLogScopeMiddleware> logger,
    IOptions<AdminOptions> adminOptions)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (QueenZoneHealthEndpoints.IsProbePath(context.Request.Path))
        {
            await next(context);
            return;
        }

        var state = new Dictionary<string, object?>
        {
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
        };
        var memberId = ForumMember.GetMemberId(context.User);
        if (memberId is Guid id)
        {
            state["MemberId"] = id.ToString("D");
        }

        // Admin staff are a small, already-identified set (unlike anonymous visitors, whom this
        // app deliberately avoids fingerprinting elsewhere — see client IP masking in
        // Program.cs), so tagging their requests costs no extra privacy exposure and lets a
        // warning/exception logged mid-request (e.g. an antiforgery rejection) be tied back to
        // who hit it.
        if (AdminAllowlist.IsAllowed(context.User, adminOptions.Value))
        {
            state["IsAdmin"] = true;
            state["AdminEmailFingerprint"] = LogRedaction.EmailFingerprint(
                AdminAllowlist.ResolveEmail(context.User));
        }

        using (logger.BeginScope(state))
        {
            await next(context);
        }
    }
}

public static class RequestLogScopeExtensions
{
    public static IApplicationBuilder UseRequestLogScope(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<RequestLogScopeMiddleware>();
    }
}
