namespace QueenZone.Web;

public static class PublicOutputCachePolicies
{
    public const string PublicSitemaps = "public-sitemaps";

    /// <summary>Anonymous public HTML (Razor Pages) short TTL cache.</summary>
    public const string PublicHtml = "public-html";

    /// <summary>
    /// Output-cache tag applied to robots/sitemap routes. Evict with
    /// <see cref="Microsoft.AspNetCore.OutputCaching.IOutputCacheStore.EvictByTagAsync"/> after
    /// public URL sets change (for example admin news publish).
    /// </summary>
    public const string PublicSitemapTag = "public-sitemap";

    /// <summary>
    /// Output-cache tag for anonymous public HTML. Evict after editorial changes that affect
    /// visitor-facing pages (publish/unpublish/delete/edit of published news).
    /// </summary>
    public const string PublicHtmlTag = "public-html";

    public static readonly TimeSpan SitemapDuration = TimeSpan.FromHours(24);

    /// <summary>Short TTL so editorial changes without eviction still expire quickly.</summary>
    public static readonly TimeSpan HtmlDuration = TimeSpan.FromSeconds(90);

    private static readonly string[] ExcludedPathPrefixes =
    [
        "/account",
        "/admin",
        "/health",
        "/api",
        "/ugc",
        "/error",
        "/submit",
        "/help",
        "/contact",
        "/search",
        "/forum/attachment",
        "/trivia",
    ];

    /// <summary>
    /// Query values that change public HTML. Marketing/tracking parameters are deliberately
    /// omitted so they reuse the canonical page's cache entry.
    /// </summary>
    public static readonly string[] PublicHtmlQueryKeys =
    [
        "page",
        "pageNumber",
        "size",
        "slug",
        "year",
    ];

    public static bool IsPublicReadOnlyRequest(HttpContext httpContext)
    {
        if (!HttpMethods.IsGet(httpContext.Request.Method) &&
            !HttpMethods.IsHead(httpContext.Request.Method))
        {
            return false;
        }

        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return false;
        }

        var path = httpContext.Request.Path;
        return !ExcludedPathPrefixes.Any(prefix =>
            path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// True when the request may use the public HTML output-cache policy.
    /// Disabled on automated-test hosts so WebApplicationFactory/E2E suites do not share stale
    /// HTML across cases.
    /// </summary>
    public static bool IsCacheablePublicHtmlRequest(HttpContext httpContext)
    {
        var environment = httpContext.RequestServices.GetService<IHostEnvironment>();
        if (environment is not null && QueenZoneEnvironments.IsAutomatedTestHost(environment))
        {
            return false;
        }

        return IsPublicReadOnlyRequest(httpContext);
    }
}
