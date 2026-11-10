using QueenZone.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace QueenZone.Web;

/// <summary>
/// Validates raw avatar uploads and produces square WebP full + thumbnail payloads.
/// </summary>
public static class MemberAvatarImageProcessor
{
    public sealed record ProcessedAvatar(MemoryStream FullImage, MemoryStream Thumbnail);

    /// <summary>
    /// Validates size and magic-byte MIME type, then crops/resizes to square WebP outputs.
    /// Throws <see cref="MemberAvatarRejectedException"/> for validation failures (expected client errors).
    /// </summary>
    public static async Task<ProcessedAvatar> ProcessAsync(
        Stream source,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length <= 0)
        {
            throw new MemberAvatarRejectedException("An image file is required.");
        }

        if (buffer.Length > MemberAvatarPaths.MaxUploadBytes)
        {
            throw new MemberAvatarRejectedException(
                $"Avatar must be {MemberAvatarPaths.MaxUploadBytes / (1024 * 1024)} MB or smaller.");
        }

        buffer.Position = 0;
        var headerLength = (int)Math.Min(64, buffer.Length);
        var header = new byte[headerLength];
        var read = await buffer.ReadAsync(header.AsMemory(0, headerLength), cancellationToken);
        buffer.Position = 0;

        var sniffed = BlobContentSniffer.TryDetectContentType(header.AsSpan(0, read));
        if (sniffed is null || !MemberAvatarPaths.AllowedContentTypes.Contains(sniffed))
        {
            throw new MemberAvatarRejectedException("Avatar must be a JPEG, PNG, or WebP image.");
        }

        // Extension must agree when present (same policy as blob upload validator).
        var extension = Path.GetExtension(originalFileName);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            var fromExt = BlobContentSniffer.GuessContentTypeFromExtension(extension);
            if (fromExt is not null
                && !string.Equals(fromExt, sniffed, StringComparison.OrdinalIgnoreCase))
            {
                throw new MemberAvatarRejectedException("File extension does not match the image content.");
            }
        }

        try
        {
            using var image = await Image.LoadAsync(buffer, cancellationToken);
            var full = await EncodeSquareAsync(image, MemberAvatarPaths.FullSizePixels, cancellationToken);
            var thumb = await EncodeSquareAsync(image, MemberAvatarPaths.ThumbSizePixels, cancellationToken);
            return new ProcessedAvatar(full, thumb);
        }
        catch (UnknownImageFormatException)
        {
            throw new MemberAvatarRejectedException("Avatar must be a JPEG, PNG, or WebP image.");
        }
        catch (InvalidImageContentException)
        {
            throw new MemberAvatarRejectedException("Avatar image could not be read.");
        }
    }

    private static async Task<MemoryStream> EncodeSquareAsync(
        Image image,
        int size,
        CancellationToken cancellationToken)
    {
        using var clone = image.Clone(ctx =>
        {
            ctx.Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center,
            });
        });

        var output = new MemoryStream();
        await clone.SaveAsync(output, new WebpEncoder { Quality = 85 }, cancellationToken);
        output.Position = 0;
        return output;
    }
}

/// <summary>
/// An avatar upload failed validation. The message is written for the member and is safe to show;
/// other exceptions are never surfaced to the member as rejections (#2436).
/// </summary>
public sealed class MemberAvatarRejectedException(string message) : Exception(message);
