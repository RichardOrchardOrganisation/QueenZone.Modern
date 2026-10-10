using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.OutputCaching;

namespace QueenZone.Web;

public static class PublicOutputCachePolicies
{
    public const string PublicSitemaps = "public-sitemaps";

    /// <summary>Anonymous public HTML (Razor Pages) short TTL cache.</summary>
    public const string PublicHtml = "public-html";

    public const string PublicArchiveAuthors = "public-archive-authors";

    public static readonly TimeSpan ArchiveAuthorsDuration = TimeSpan.FromMinutes(10);

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

    public static PageActionEndpointConventionBuilder CachePublicHtml(this PageActionEndpointConventionBuilder endpoints)
    {
        endpoints.Add(endpoint =>
        {
            var page = endpoint.Metadata.OfType<PageActionDescriptor>().LastOrDefault();
            endpoint.Metadata.Add(new OutputCacheAttribute
            {
                PolicyName = page?.ViewEnginePath == "/Forum/ArchiveAuthor" ? PublicArchiveAuthors : PublicHtml,
            });
        });
        return endpoints;
    }

    /// <summary>
    /// Render a cold forum HEAD as GET so it populates the same complete cache entry.
    /// Discard only the wire body, outside output caching; never skip the page lookup.
    /// ASP.NET Core's default cache key includes Request.Method.
    /// </summary>
    public static async Task ShareForumHeadCacheAsync(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path;
        if (!HttpMethods.IsHead(context.Request.Method) ||
            !IsCacheablePublicHtmlRequest(context) ||
            !(path.StartsWithSegments("/forum/topic", StringComparison.OrdinalIgnoreCase) ||
              path.StartsWithSegments("/forum/archive-authors", StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var method = context.Request.Method;
        var body = context.Response.Body;
        try
        {
            context.Request.Method = HttpMethods.Get;
            context.Response.Body = Stream.Null;
            await next(context);
        }
        finally
        {
            context.Request.Method = method;
            context.Response.Body = body;
        }
    }

    private static readonly string[] ExcludedPathPrefixes =
    [
        "/account",
        "/appearance",
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
    /// omitted so they reuse the canonical page's cache entry. Keep this contract in sync
    /// with public page inputs and the production variant tests; see
    /// docs/architecture/hosting-scale-and-cache.md (Public HTML query variation).
    /// </summary>
    public static readonly string[] PublicHtmlQueryKeys =
    [
        "page",
        "pageNumber",
        "size",
        "slug",
        "year",
        "decade", // Timeline selection.
        "cp", // Community article pagination.
        "tag", // Community article filtering.
        "scope", // Daily, best-run, and total-points leaderboards.
        "claim", // Quiz sprint guest-score claim notice.
        "handler", // Razor Pages named GET handlers must not reuse the default response.
    ];

    public static bool IsPublicReadOnlyRequest(HttpContext httpContext)
    {
        if (!HttpMethods.IsGet(httpContext.Request.Method) &&
            !HttpMethods.IsHead(httpContext.Request.Method))
        {
            return false;
        }

        if (httpContext.User.Identity?.IsAuthenticated == true || DeviceThemeCookie.Read(httpContext.Request) is not null)
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
