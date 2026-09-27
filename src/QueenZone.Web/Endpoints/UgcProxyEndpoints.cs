using Microsoft.AspNetCore.Mvc;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Serves private UGC blobs through the web app (no direct Azure blob URLs in HTML).
/// </summary>
public static class UgcProxyEndpoints
{
    /// <summary>
    /// Blob names are unique/immutable (GUID-based). Safe for long-lived public caches.
    /// </summary>
    public const string CacheControlHeaderValue = "public, max-age=604800, immutable";

    public static void MapUgcProxyEndpoints(this WebApplication app)
    {
        app.MapGet(UgcProxyPaths.RouteTemplate, async (
                string area,
                string blobName,
                [FromQuery] string? size,
                [FromServices] IBlobUploadService blobUploadService,
                CancellationToken cancellationToken) =>
            await ServeAsync(area, blobName, size, blobUploadService, cancellationToken))
            .WithName("UgcProxy")
            .AllowAnonymous();
    }

    internal static async Task<IResult> ServeAsync(
        string area,
        string blobName,
        string? size,
        IBlobUploadService blobUploadService,
        CancellationToken cancellationToken)
    {
        var container = UgcProxyPaths.TryMapAreaToContainer(area);
        if (container is null || string.IsNullOrWhiteSpace(blobName))
        {
            return Results.NotFound();
        }

        // Normalize catch-all path segments.
        var normalized = blobName.Trim().TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains('\\', StringComparison.Ordinal))
        {
            return Results.NotFound();
        }

        var useThumb = string.Equals(size, "thumb", StringComparison.OrdinalIgnoreCase);
        var resolvedName = useThumb
            ? UgcProxyPaths.ToThumbBlobName(normalized)
            : normalized;

        try
        {
            var content = await blobUploadService.OpenReadAsync(
                container,
                resolvedName,
                cancellationToken);

            // A missing thumb is not the original. Caching that fallback would pin a
            // full-size image under the thumb URL for a week.
            if (content is null)
            {
                return Results.NotFound();
            }

            // Anonymous /ugc serves images only. Documents download through the member route.
            if (!IsAnonymousImageContentType(content.ContentType))
            {
                await content.DisposeAsync();
                return Results.NotFound();
            }

            return new CachedBlobStreamResult(content.Stream, content.ContentType);
        }
        catch (NotSupportedException)
        {
            return Results.NotFound();
        }
    }

    private static readonly HashSet<string> AnonymousImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/webp",
        "image/jpeg",
        "image/png",
        "image/gif",
    };

    /// <summary>
    /// Image types the anonymous proxy may render inline. Anything else is a download.
    /// </summary>
    internal static bool IsAnonymousImageContentType(string? contentType) =>
        contentType is not null
        && AnonymousImageContentTypes.Contains(contentType);

    /// <summary>Streams a UGC blob with long-lived Cache-Control for anonymous CDN/browser reuse.</summary>
    internal sealed class CachedBlobStreamResult(Stream stream, string contentType) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentType = contentType;
            httpContext.Response.Headers.CacheControl = CacheControlHeaderValue;

            try
            {
                await stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
            }
            finally
            {
                await stream.DisposeAsync();
            }
        }
    }
}
