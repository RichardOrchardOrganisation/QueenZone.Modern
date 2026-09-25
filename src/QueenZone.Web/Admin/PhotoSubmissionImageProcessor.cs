using QueenZone.Storage;
using SixLabors.ImageSharp;

namespace QueenZone.Web;

/// <summary>
/// Validates member photo submissions and produces original + web + thumbnail payloads.
/// Public derivatives default to WebP via <see cref="PhotoWebpDerivatives"/>.
/// </summary>
public static class PhotoSubmissionImageProcessor
{
    public const long MaxUploadBytes = 20 * 1024 * 1024;

    public const int WebMaxLongestSide = PhotoWebpDerivatives.DefaultWebMaxLongestSide;

    public const int ThumbSizePixels = PhotoWebpDerivatives.DefaultThumbSizePixels;

    public static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/tiff",
    };

    public sealed record ProcessedPhotoSubmission(
        MemoryStream Original,
        MemoryStream WebOptimized,
        MemoryStream Thumbnail,
        string MimeType,
        int WidthPx,
        int HeightPx,
        long OriginalSizeBytes);

    /// <summary>
    /// Validates size and MIME type, keeps the original bytes, and generates WebP derivatives.
    /// Throws <see cref="InvalidOperationException"/> for expected client errors.
    /// </summary>
    public static async Task<ProcessedPhotoSubmission> ProcessAsync(
        Stream source,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        return await ImageUploadPipeline.ProcessAsync(
            source,
            originalFileName,
            MaxUploadBytes,
            new ImageUploadPipeline.Policy(
                AllowedContentTypes.Contains,
                RequireAllowedExtension: false,
                "A photo file is required.",
                $"Photo must be {MaxUploadBytes / (1024 * 1024)} MB or smaller.",
                "Photo must be a JPEG, PNG, WebP, or TIFF image.",
                "Photo image could not be read."),
            async (image, buffer, contentType, token) =>
            {
                var width = image.Width;
                var height = image.Height;

                var web = await PhotoWebpDerivatives.CreateMaxSideAsync(
                    image,
                    WebMaxLongestSide,
                    cancellationToken: token);
                var thumb = await PhotoWebpDerivatives.CreateSquareThumbnailAsync(
                    image,
                    ThumbSizePixels,
                    cancellationToken: token);

                var original = new MemoryStream();
                buffer.Position = 0;
                await buffer.CopyToAsync(original, token);
                original.Position = 0;

                return new ProcessedPhotoSubmission(
                    original,
                    web.Stream,
                    thumb.Stream,
                    contentType,
                    width,
                    height,
                    original.Length);
            },
            cancellationToken);
    }
}
