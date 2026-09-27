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

        // Allowlisted admin requests get IsAdmin so a mid-request warning/exception can be
        // distinguished from visitor traffic without putting email (or an email-derived
        // fingerprint) in the log scope.
        if (AdminAllowlist.IsAllowed(context.User, adminOptions.Value))
        {
            state["IsAdmin"] = true;
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
