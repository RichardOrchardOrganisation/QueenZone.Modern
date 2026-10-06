using QueenZone.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace QueenZone.Web;

/// <summary>
/// Validates album and single cover uploads and produces square WebP derivatives:
/// a full cover (longest side <see cref="FullMaxSide"/>) and a <see cref="ThumbSize"/>
/// thumbnail for the discography grid. Uses the same crop dialog as news images,
/// locked to 1:1. A missing or invalid crop falls back to a centred square.
/// </summary>
public static class DiscographyCoverImageProcessor
{
    public const int AspectWidth = 1;

    public const int AspectHeight = 1;

    public const int MinCropSize = 300;

    public const int FullMaxSide = 1200;

    public const int ThumbSize = 300;

    public const long MaxUploadBytes = 10 * 1024 * 1024;

    public const double CropAspectTolerance = 0.05;

    public sealed record ProcessedCover(
        MemoryStream Full,
        int FullWidth,
        int FullHeight,
        MemoryStream Thumbnail,
        int ThumbWidth,
        int ThumbHeight) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Full.DisposeAsync();
            await Thumbnail.DisposeAsync();
        }
    }

    public static Task<ProcessedCover> ProcessAsync(
        Stream source,
        string originalFileName,
        NewsArticleImageCrop? crop,
        CancellationToken cancellationToken = default) =>
        ImageUploadPipeline.ProcessAsync(
            source,
            originalFileName,
            MaxUploadBytes,
            new ImageUploadPipeline.Policy(
                contentType => NewsArticleImageProcessor.AllowedContentTypes.Contains(
                    string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : contentType),
                RequireAllowedExtension: true,
                "A cover image is required.",
                $"Cover image must be {MaxUploadBytes / (1024 * 1024)} MB or smaller.",
                "Cover image must be a JPEG, PNG, or WebP file.",
                "Cover image could not be read."),
            async (image, _, _, token) =>
            {
                var rect = ResolveCrop(image.Width, image.Height, crop);
                if (rect.Width < MinCropSize || rect.Height < MinCropSize)
                {
                    throw new InvalidOperationException(
                        $"Cover image is too small. Use at least {MinCropSize}×{MinCropSize} pixels.");
                }

                using var cropped = image.Clone(ctx => ctx.Crop(rect));
                var full = await PhotoWebpDerivatives.CreateMaxSideAsync(cropped, FullMaxSide, cancellationToken: token);
                var thumb = await PhotoWebpDerivatives.CreateMaxSideAsync(cropped, ThumbSize, cancellationToken: token);
                return new ProcessedCover(
                    ToMemoryStream(full.Stream),
                    full.WidthPx,
                    full.HeightPx,
                    ToMemoryStream(thumb.Stream),
                    thumb.WidthPx,
                    thumb.HeightPx);
            },
            cancellationToken);

    internal static Rectangle ResolveCrop(int width, int height, NewsArticleImageCrop? requested)
    {
        if (requested is not NewsArticleImageCrop crop
            || crop.X < 0
            || crop.Y < 0
            || crop.Width < 1
            || crop.Height < 1
            || crop.X + crop.Width > width
            || crop.Y + crop.Height > height
            || Math.Abs((crop.Width / (double)crop.Height) - 1) > CropAspectTolerance)
        {
            return CenterSquare(width, height);
        }

        return new Rectangle(crop.X, crop.Y, crop.Width, crop.Height);
    }

    internal static Rectangle CenterSquare(int width, int height)
    {
        var side = Math.Max(0, Math.Min(width, height));
        return new Rectangle((width - side) / 2, (height - side) / 2, side, side);
    }

    private static MemoryStream ToMemoryStream(Stream source)
    {
        if (source is MemoryStream memory)
        {
            memory.Position = 0;
            return memory;
        }

        var copy = new MemoryStream();
        source.Position = 0;
        source.CopyTo(copy);
        copy.Position = 0;
        source.Dispose();
        return copy;
    }
}
