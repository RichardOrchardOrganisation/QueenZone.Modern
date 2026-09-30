using QueenZone.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace QueenZone.Web;

/// <summary>
/// Validates editor image uploads and produces full + thumbnail WebP payloads
/// (max longest side from <see cref="UgcProxyPaths"/>).
/// </summary>
public static class EditorImageProcessor
{
    public sealed record ProcessedEditorImage(
        MemoryStream FullImage,
        MemoryStream Thumbnail,
        string FullFileName,
        string ThumbFileName);

    public static Task<ProcessedEditorImage> ProcessAsync(
        Stream source,
        string originalFileName,
        CancellationToken cancellationToken = default) =>
        ProcessAsync(source, originalFileName, EditorImageUploadEndpoints.MaxImageBytes, cancellationToken);

    public static async Task<ProcessedEditorImage> ProcessAsync(
        Stream source,
        string originalFileName,
        long maxBytes,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0)
        {
            maxBytes = EditorImageUploadEndpoints.MaxImageBytes;
        }

        return await ImageUploadPipeline.ProcessAsync(
            source,
            originalFileName,
            maxBytes,
            new ImageUploadPipeline.Policy(
                contentType => contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase),
                RequireAllowedExtension: false,
                "An image file is required.",
                $"Image must be {maxBytes} bytes or smaller.",
                "Only image uploads are allowed.",
                "Image could not be read."),
            async (image, _, _, token) =>
            {
                var full = await EncodeMaxSideAsync(image, UgcProxyPaths.FullMaxLongestSide, token);
                var thumb = await EncodeMaxSideAsync(image, UgcProxyPaths.ThumbMaxLongestSide, token);

                var baseName = Path.GetFileNameWithoutExtension(
                    string.IsNullOrWhiteSpace(originalFileName) ? "paste" : originalFileName);
                if (string.IsNullOrWhiteSpace(baseName))
                {
                    baseName = "paste";
                }

                // Storage uses generated Guid names; these are only used when PreferredBlobName is not set.
                var fullFileName = baseName + ".webp";
                var thumbFileName = baseName + "-thumb.webp";

                return new ProcessedEditorImage(full, thumb, fullFileName, thumbFileName);
            },
            cancellationToken);
    }

    private static async Task<MemoryStream> EncodeMaxSideAsync(
        Image image,
        int maxLongestSide,
        CancellationToken cancellationToken)
    {
        using var clone = image.Clone(ctx =>
        {
            if (image.Width > maxLongestSide || image.Height > maxLongestSide)
            {
                ctx.Resize(new ResizeOptions
                {
                    Size = new Size(maxLongestSide, maxLongestSide),
                    Mode = ResizeMode.Max,
                });
            }
        });

        var output = new MemoryStream();
        await clone.SaveAsync(output, new WebpEncoder { Quality = 85 }, cancellationToken);
        output.Position = 0;
        return output;
    }
}
