namespace QueenZone.Web;

/// <summary>
/// Short browser/CDN Cache-Control for anonymous public HTML documents.
/// Complements server-side output caching (<see cref="PublicOutputCachePolicies"/>):
/// when a response is not (or not yet) output-cached, intermediaries and browsers still
/// get a brief reusable cache window without long-lived stale HTML.
/// </summary>
public static class PublicHtmlCacheControl
{
    /// <summary>
    /// 60s public max-age — under the 90s HTML output-cache TTL so browser reuse stays short.
    /// </summary>
    public const string HeaderValue = "public, max-age=60";

    /// <summary>
    /// Applies <see cref="HeaderValue"/> when the response is a successful anonymous public
    /// HTML document that does not already set Cache-Control (static files, sitemaps, UGC).
    /// </summary>
    public static bool TryApply(HttpContext context)
    {
        if (context.Response.HasStarted)
        {
            return false;
        }

        if (!HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method))
        {
            return false;
        }

        if (context.Response.StatusCode is < StatusCodes.Status200OK
            or >= StatusCodes.Status300MultipleChoices)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(context.Response.Headers.CacheControl))
        {
            return false;
        }

        var contentType = context.Response.ContentType;
        if (contentType is null
            || contentType.IndexOf("text/html", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        if (DeviceThemeCookie.Read(context.Request) is not null)
        {
            context.Response.Headers.CacheControl = "private, no-store";
            return false;
        }

        if (!PublicOutputCachePolicies.IsPublicReadOnlyRequest(context))
        {
            return false;
        }

        context.Response.Headers.CacheControl = HeaderValue;
        return true;
    }
}
