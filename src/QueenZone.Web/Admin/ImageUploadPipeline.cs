using QueenZone.Storage;
using SixLabors.ImageSharp;

namespace QueenZone.Web;

/// <summary>
/// Reads and decodes an image once. Callers provide their own MIME policy,
/// limits, error text, and derivative generation.
/// </summary>
internal static class ImageUploadPipeline
{
    internal sealed record Policy(
        Func<string, bool> AllowsContentType,
        bool RequireAllowedExtension,
        string EmptyError,
        string TooLargeError,
        string InvalidTypeError,
        string InvalidContentError);

    internal static async Task<T> ProcessAsync<T>(
        Stream source,
        string originalFileName,
        long maxBytes,
        Policy policy,
        Func<Image, MemoryStream, string, CancellationToken, Task<T>> process,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length <= 0)
        {
            throw new InvalidOperationException(policy.EmptyError);
        }

        if (buffer.Length > maxBytes)
        {
            throw new InvalidOperationException(policy.TooLargeError);
        }

        buffer.Position = 0;
        var headerLength = (int)Math.Min(64, buffer.Length);
        var header = new byte[headerLength];
        var read = await buffer.ReadAsync(header.AsMemory(0, headerLength), cancellationToken);
        buffer.Position = 0;

        var sniffed = BlobContentSniffer.TryDetectContentType(header.AsSpan(0, read));
        if (sniffed is null || !policy.AllowsContentType(sniffed))
        {
            throw new InvalidOperationException(policy.InvalidTypeError);
        }

        var extension = Path.GetExtension(originalFileName);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            var fromExtension = BlobContentSniffer.GuessContentTypeFromExtension(extension);
            if (fromExtension is not null)
            {
                if (policy.RequireAllowedExtension && !policy.AllowsContentType(fromExtension))
                {
                    throw new InvalidOperationException(policy.InvalidTypeError);
                }

                if (!policy.RequireAllowedExtension
                    && !fromExtension.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("File extension does not match the image content.");
                }

                if (!ContentTypesAgree(fromExtension, sniffed))
                {
                    throw new InvalidOperationException("File extension does not match the image content.");
                }
            }
        }

        try
        {
            using var image = await Image.LoadAsync(buffer, cancellationToken);
            return await process(image, buffer, sniffed, cancellationToken);
        }
        catch (UnknownImageFormatException)
        {
            throw new InvalidOperationException(policy.InvalidTypeError);
        }
        catch (InvalidImageContentException)
        {
            throw new InvalidOperationException(policy.InvalidContentError);
        }
    }

    private static bool ContentTypesAgree(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
        || (IsJpegFamily(left) && IsJpegFamily(right));

    private static bool IsJpegFamily(string contentType) =>
        string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase);
}
