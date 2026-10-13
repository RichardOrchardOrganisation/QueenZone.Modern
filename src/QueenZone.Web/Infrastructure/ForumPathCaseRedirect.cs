namespace QueenZone.Web;

/// <summary>
/// 301s mixed-case forum topic/category page paths (for example <c>/FORUM/TOPIC/1/Slug</c>)
/// to the lowercase canonical URL. Razor Pages routing is case-insensitive, so without this
/// every casing serves a duplicate 200. Forum attachment, poll, and post-action paths are left
/// alone. The redirect is never edge-cacheable, consistent with #2240.
/// </summary>
public static class ForumPathCaseRedirect
{
    public static IApplicationBuilder UseForumPathCaseRedirect(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (TryBuildRedirectLocation(context.Request, out var location))
            {
                context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                context.Response.Headers.Location = location;
                context.Response.Headers.CacheControl = "private, no-store";
                return;
            }

            await next();
        });

    internal static bool TryBuildRedirectLocation(HttpRequest request, out string location)
    {
        location = string.Empty;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        var path = request.Path;
        if (!path.StartsWithSegments("/forum", StringComparison.OrdinalIgnoreCase)
            || !IsRedirectableForumPath(path))
        {
            return false;
        }

        var value = path.Value!;
        var lower = value.ToLowerInvariant();
        if (string.Equals(value, lower, StringComparison.Ordinal))
        {
            return false;
        }

        location = request.PathBase.Add(new PathString(lower)).ToUriComponent() + request.QueryString.ToUriComponent();
        return true;
    }

    private static bool IsRedirectableForumPath(PathString path) =>
        path.StartsWithSegments("/forum/topic", StringComparison.OrdinalIgnoreCase)
        || (path.Value!.Length > "/forum/".Length && char.IsAsciiDigit(path.Value[7]));
}
