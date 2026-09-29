using Microsoft.Extensions.Options;

namespace QueenZone.Web.Sitemap;

public static class SitemapEndpoints
{
    public static void MapSitemapEndpoints(this WebApplication app)
    {
        app.MapGet("/robots.txt", (IOptions<SiteOptions> options) =>
        {
            var baseUrl = options.Value.PublicBaseUrl.TrimEnd('/');
            var body = $"""
                User-agent: *
                Allow: /

                Disallow: /admin/
                Disallow: /health
                Disallow: /search
                Disallow: /api/v1/search

                Sitemap: {baseUrl}/sitemap.xml
                """;

            return Results.Text(body, "text/plain");
        })
        .CacheOutput(PublicOutputCachePolicies.PublicSitemaps);

        app.MapGet("/sitemap.xml", async (
            SitemapIndexBuilder indexBuilder,
            IOptions<SiteOptions> options,
            CancellationToken cancellationToken) =>
        {
            var entries = await indexBuilder.BuildAsync(cancellationToken);
            var xml = SitemapXmlWriter.WriteSitemapIndex(entries, options.Value.PublicBaseUrl);
            return Results.Content(xml, "application/xml; charset=utf-8");
        })
        .CacheOutput(PublicOutputCachePolicies.PublicSitemaps);

        app.MapGet("/sitemap-forum-{fileNumber:int}.xml", async (
            int fileNumber,
            ForumSitemapBuilder builder,
            IOptions<SiteOptions> options,
            IOptions<SitemapOptions> sitemapOptions,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            ApplyPublicCacheHeader(httpContext, sitemapOptions.Value);

            var entries = await builder.BuildFileAsync(fileNumber, cancellationToken);
            if (entries is null || entries.Count == 0)
            {
                return Results.NotFound();
            }

            var xml = SitemapXmlWriter.WriteUrlSet(entries, options.Value.PublicBaseUrl);
            return Results.Content(xml, "application/xml; charset=utf-8");
        })
        .CacheOutput(PublicOutputCachePolicies.PublicSitemaps);

        foreach (var section in SitemapSections.All)
        {
            var capturedSection = section;
            app.MapGet(SitemapSections.GetPath(capturedSection), async (
                HttpContext httpContext,
                CoreSitemapBuilder builder,
                IOptions<SiteOptions> options,
                IOptions<SitemapOptions> sitemapOptions,
                CancellationToken cancellationToken) =>
            {
                ApplyPublicCacheHeader(httpContext, sitemapOptions.Value);

                var entries = await builder.BuildSectionAsync(capturedSection, cancellationToken);
                if (entries is null)
                {
                    return Results.NotFound();
                }

                var xml = SitemapXmlWriter.WriteUrlSet(entries, options.Value.PublicBaseUrl);
                return Results.Content(xml, "application/xml; charset=utf-8");
            })
            .CacheOutput(PublicOutputCachePolicies.PublicSitemaps);
        }

        app.MapGet("/sitemap-core.xml", async (
            HttpContext httpContext,
            CoreSitemapService sitemapService,
            IOptions<SiteOptions> options,
            IOptions<SitemapOptions> sitemapOptions,
            CancellationToken cancellationToken) =>
        {
            ApplyPublicCacheHeader(httpContext, sitemapOptions.Value);

            var xml = await sitemapService.GetXmlAsync(options.Value.PublicBaseUrl, cancellationToken);
            return Results.Content(xml, "application/xml; charset=utf-8");
        })
        .CacheOutput(PublicOutputCachePolicies.PublicSitemaps);
    }

    private static void ApplyPublicCacheHeader(HttpContext httpContext, SitemapOptions sitemapOptions)
    {
        var maxAgeSeconds = Math.Max(sitemapOptions.CacheHours, 1) * 3600;
        httpContext.Response.Headers.CacheControl = $"public, max-age={maxAgeSeconds}";
    }
}
