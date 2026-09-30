using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using QueenZone.Storage;

namespace QueenZone.Web;

public static class EditorImageUploadEndpoints
{
    public const string Route = "/api/uploads/editor-image";

    /// <summary>Legacy constant kept for tests; runtime uses <see cref="BlobUploadOptions.EditorMaxBytes"/>.</summary>
    public const long MaxImageBytes = 10 * 1024 * 1024;

    public const string AntiforgeryHeaderName = "RequestVerificationToken";

    public static readonly IReadOnlySet<string> AllowedContainers =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BlobUploadContainers.Forum,
            BlobUploadContainers.Articles,
            BlobUploadContainers.Photos,
            BlobUploadContainers.Avatars,
        };

    public static void MapEditorImageUploadEndpoints(this WebApplication app)
    {
        app.MapPost(Route, async (
                HttpContext httpContext,
                IBlobUploadService blobUploadService,
                IAntiforgery antiforgery,
                IOptions<BlobUploadOptions> blobUploadOptions,
                MemberUploadQuotaService uploadQuota,
                CancellationToken cancellationToken) =>
            await UploadAsync(
                httpContext,
                blobUploadService,
                antiforgery,
                blobUploadOptions.Value,
                uploadQuota,
                cancellationToken))
            .RequireAuthorization("Authoring")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.Upload)
            .DisableAntiforgery()
            .WithName("UploadEditorImage")
            .Accepts<IFormFile>("multipart/form-data");
    }

    /// <summary>
    /// Test/helper entry that supplies an already-bound file.
    /// Production path reads the multipart form itself so antiforgery can see the token field.
    /// </summary>
    internal static Task<IResult> UploadAsync(
        HttpContext httpContext,
        IFormFile? file,
        string? container,
        IBlobUploadService blobUploadService,
        IAntiforgery antiforgery,
        CancellationToken cancellationToken)
    {
        var uploadQuota = httpContext.RequestServices.GetService<MemberUploadQuotaService>()
            ?? CreatePassthroughQuota();
        return UploadCoreAsync(
            httpContext,
            file,
            container,
            blobUploadService,
            antiforgery,
            new BlobUploadOptions { EditorMaxBytes = MaxImageBytes },
            uploadQuota,
            skipAntiforgery: false,
            cancellationToken);
    }

    internal static async Task<IResult> UploadAsync(
        HttpContext httpContext,
        IBlobUploadService blobUploadService,
        IAntiforgery antiforgery,
        BlobUploadOptions blobUploadOptions,
        MemberUploadQuotaService uploadQuota,
        CancellationToken cancellationToken)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        // Ensure multipart form (including __RequestVerificationToken) is available before AF check.
        IFormCollection form;
        try
        {
            form = await httpContext.Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            return Results.BadRequest(new { error = "Invalid multipart form data." });
        }

        if (!await antiforgery.IsRequestValidAsync(httpContext))
        {
            return Results.BadRequest(new { error = "Invalid antiforgery token." });
        }

        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        var container = form.TryGetValue("container", out var containerValues)
            ? containerValues.ToString()
            : null;

        return await UploadCoreAsync(
            httpContext,
            file,
            container,
            blobUploadService,
            antiforgery,
            blobUploadOptions,
            uploadQuota,
            skipAntiforgery: true,
            cancellationToken);
    }

    private static async Task<IResult> UploadCoreAsync(
        HttpContext httpContext,
        IFormFile? file,
        string? container,
        IBlobUploadService blobUploadService,
        IAntiforgery antiforgery,
        BlobUploadOptions blobUploadOptions,
        MemberUploadQuotaService uploadQuota,
        bool skipAntiforgery,
        CancellationToken cancellationToken)
    {
        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        if (!skipAntiforgery)
        {
            try
            {
                await antiforgery.ValidateRequestAsync(httpContext);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest(new { error = "Invalid antiforgery token." });
            }
        }

        if (file is null || file.Length <= 0)
        {
            return Results.BadRequest(new { error = "A file is required." });
        }

        var containerName = ResolveContainer(container);

        if (!AllowedContainers.Contains(containerName)
            || UgcProxyPaths.TryMapContainerToArea(containerName) is null)
        {
            return Results.BadRequest(new { error = "Container is not allowed for editor uploads." });
        }

        var maxBytes = ResolveMaxBytes(blobUploadOptions, containerName);
        if (file.Length > maxBytes)
        {
            return Results.BadRequest(new { error = $"File must be {maxBytes} bytes or smaller." });
        }

        var principalKey = MemberUploadQuotaService.PrincipalKeyFromUser(httpContext.User);
        if (!uploadQuota.TryConsume(principalKey, file.Length, out var quotaError))
        {
            return Results.Json(new { error = quotaError }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        var context = ImageUploadContextFactory.Create(httpContext.User);
        var contentTypeHeader = file.ContentType ?? string.Empty;
        var isImage = contentTypeHeader.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || LooksLikeImageFileName(file.FileName);

        try
        {
            if (isImage)
            {
                return await UploadImageAsync(
                    file,
                    containerName,
                    context,
                    blobUploadService,
                    maxBytes,
                    cancellationToken);
            }

            return await UploadAttachmentAsync(
                file,
                containerName,
                context,
                blobUploadService,
                blobUploadOptions,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (NotSupportedException ex)
        {
            return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (BlobUploadException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static MemberUploadQuotaService CreatePassthroughQuota() =>
        new(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new UploadQuotaOptions { Enabled = false }));

    private static async Task<IResult> UploadImageAsync(
        IFormFile file,
        string containerName,
        BlobUploadContext context,
        IBlobUploadService blobUploadService,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        string? fullBlobName = null;
        string? thumbBlobName = null;

        try
        {
            await using var source = file.OpenReadStream();
            var processed = await EditorImageProcessor.ProcessAsync(
                source,
                file.FileName,
                maxBytes,
                cancellationToken);

            await using (processed.FullImage)
            await using (processed.Thumbnail)
            {
                fullBlobName = BuildBlobName(context, ".webp");
                thumbBlobName = UgcProxyPaths.ToThumbBlobName(fullBlobName);

                var fullResult = await blobUploadService.UploadAsync(
                    processed.FullImage,
                    fullBlobName,
                    containerName,
                    ImageUploadContextFactory.WithPreferredBlobName(context, fullBlobName),
                    cancellationToken);

                fullBlobName = fullResult.BlobName;
                thumbBlobName = UgcProxyPaths.ToThumbBlobName(fullBlobName);

                processed.Thumbnail.Position = 0;
                await blobUploadService.UploadAsync(
                    processed.Thumbnail,
                    thumbBlobName,
                    containerName,
                    ImageUploadContextFactory.WithPreferredBlobName(context, thumbBlobName),
                    cancellationToken);

                var url = UgcProxyPaths.GetPath(fullResult.Container, fullBlobName);
                var thumbUrl = UgcProxyPaths.GetPath(fullResult.Container, thumbBlobName);

                return Results.Json(new
                {
                    url,
                    thumbUrl,
                    kind = "image",
                    fileName = Path.GetFileName(file.FileName),
                    container = fullResult.Container,
                    blobName = fullBlobName,
                });
            }
        }
        catch (BlobUploadException)
        {
            await TryDeleteQuietlyAsync(blobUploadService, containerName, fullBlobName, cancellationToken);
            await TryDeleteQuietlyAsync(blobUploadService, containerName, thumbBlobName, cancellationToken);
            throw;
        }
    }

    private static async Task<IResult> UploadAttachmentAsync(
        IFormFile file,
        string containerName,
        BlobUploadContext context,
        IBlobUploadService blobUploadService,
        BlobUploadOptions blobUploadOptions,
        CancellationToken cancellationToken)
    {
        // Buffer so we can sniff magic bytes before (and during) storage validation.
        await using var buffer = new MemoryStream();
        await using (var source = file.OpenReadStream())
        {
            await source.CopyToAsync(buffer, cancellationToken);
        }

        if (buffer.Length <= 0)
        {
            return Results.BadRequest(new { error = "A file is required." });
        }

        buffer.Position = 0;
        var headerLength = (int)Math.Min(64, buffer.Length);
        var header = new byte[headerLength];
        _ = await buffer.ReadAsync(header.AsMemory(0, headerLength), cancellationToken);
        buffer.Position = 0;

        try
        {
            var validator = new BlobUploadValidator(blobUploadOptions);
            validator.EnsureKnownContainer(containerName);
            validator.ValidateSize(buffer.Length, containerName);
            validator.ResolveAndValidateContentType(file.FileName, header, containerName);
        }
        catch (BlobUploadException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || extension.Length > 16)
        {
            extension = string.Empty;
        }

        var blobName = BuildBlobName(context, extension.ToLowerInvariant());
        var result = await blobUploadService.UploadAsync(
            buffer,
            file.FileName,
            containerName,
            ImageUploadContextFactory.WithPreferredBlobName(context, blobName),
            cancellationToken);

        var url = UgcProxyPaths.GetPath(result.Container, result.BlobName);
        var displayName = string.IsNullOrWhiteSpace(file.FileName) ? "attachment" : Path.GetFileName(file.FileName);

        return Results.Json(new
        {
            url,
            kind = "file",
            fileName = displayName,
            container = result.Container,
            blobName = result.BlobName,
        });
    }

    /// <summary>
    /// Accepts blob container names (<c>ugc-articles</c>) and area aliases (<c>articles</c>).
    /// Omitted/blank values keep the forum default — callers that mean articles must send
    /// <c>container=articles</c> or <c>ugc-articles</c>.
    /// </summary>
    internal static string ResolveContainer(string? container)
    {
        var raw = string.IsNullOrWhiteSpace(container)
            ? BlobUploadContainers.Forum
            : container.Trim();
        return UgcProxyPaths.TryMapAreaToContainer(raw) ?? raw;
    }

    internal static long ResolveMaxBytes(BlobUploadOptions options, string containerName)
    {
        var editorMax = options.EditorMaxBytes > 0 ? options.EditorMaxBytes : MaxImageBytes;
        long containerMax = options.DefaultMaxBytes;
        if (options.Containers.TryGetValue(containerName, out var policy)
            && policy.MaxBytes is > 0)
        {
            containerMax = policy.MaxBytes.Value;
        }

        return Math.Min(editorMax, containerMax);
    }

    private static bool LooksLikeImageFileName(string? fileName)
    {
        var ext = Path.GetExtension(fileName ?? string.Empty);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildBlobName(BlobUploadContext context, string extension)
    {
        if (!extension.StartsWith('.') && extension.Length > 0)
        {
            extension = "." + extension;
        }

        var id = Guid.NewGuid().ToString("N");
        if (context.MemberAccountId is Guid accountId && accountId != Guid.Empty)
        {
            return $"members/{accountId:N}/{id}{extension}";
        }

        if (!string.IsNullOrWhiteSpace(context.ActorEmail))
        {
            var slug = SanitizeEmailSegment(context.ActorEmail);
            return $"editors/{slug}/{id}{extension}";
        }

        return $"anonymous/{id}{extension}";
    }

    private static string SanitizeEmailSegment(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        var chars = trimmed
            .Select(ch =>
            {
                if (char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')
                {
                    return ch;
                }

                return ch is '@' or '+' ? '-' : '\0';
            })
            .Where(ch => ch != '\0')
            .ToArray();

        var result = new string(chars).Trim('-', '.');
        return string.IsNullOrEmpty(result) ? "unknown" : result;
    }

    private static async Task TryDeleteQuietlyAsync(
        IBlobUploadService blobUploadService,
        string containerName,
        string? blobName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return;
        }

        try
        {
            await blobUploadService.DeleteAsync(containerName, blobName, cancellationToken);
        }
        catch
        {
            // Cleanup is best-effort.
        }
    }

}
