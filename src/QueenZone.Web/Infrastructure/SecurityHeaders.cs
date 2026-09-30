namespace QueenZone.Web;

/// <summary>
/// Applies baseline HTTP security headers for all responses.
/// </summary>
public static class SecurityHeaders
{
    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=()";

    /// <summary>
    /// Enforcing CSP. Inline scripts are inventoried (see issue #585) and use a
    /// per-request nonce (<see cref="CspNonce"/>) instead of 'unsafe-inline'. The Google
    /// Analytics loader script (nonce'd) creates a gtag.js &lt;script&gt; element at runtime;
    /// that element's src matches the explicit googletagmanager.com host allowance below, so it
    /// loads without needing 'strict-dynamic' — which would otherwise disable the 'self'
    /// host-allowlist for every other &lt;script src&gt; tag on the site (site.js, Quill, etc.).
    /// Inline style="" attributes have all been removed in favour of CSS classes (or, for
    /// genuinely dynamic values such as poll bar widths, a data-* attribute applied via
    /// site.js CSSOM calls, which style-src does not govern) — see issue #608 — so style-src
    /// no longer needs 'unsafe-inline'.
    /// </summary>
    public static string BuildContentSecurityPolicy(string nonce) =>
        "default-src 'self'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none'; " +
        "img-src 'self' data: blob: https://cdn.queenzone.org https://cdn2.queenzone.org https://*.blob.core.windows.net https://www.googletagmanager.com https://www.google-analytics.com; " +
        "font-src 'self' data:; " +
        "style-src 'self'; " +
        $"script-src 'self' 'nonce-{nonce}' https://www.googletagmanager.com https://www.google-analytics.com; " +
        "connect-src 'self' https://www.google-analytics.com https://*.google-analytics.com https://*.analytics.google.com https://www.googletagmanager.com https://www.google.com; " +
        "frame-src 'self' https://www.googletagmanager.com https://www.youtube-nocookie.com; " +
        "object-src 'none'";

    public static void Apply(HttpContext context)
    {
        var nonce = CspNonce.Get(context);
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = PermissionsPolicy;
        headers["Content-Security-Policy"] = BuildContentSecurityPolicy(nonce);
    }
}
