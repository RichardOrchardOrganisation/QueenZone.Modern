namespace QueenZone.Data;

public sealed record ForumPostAttachment(
    string FileName,
    long? FileSizeBytes,
    string Url,
    string? MimeType = null,
    string? ThumbnailUrl = null)
{
    public string Extension => Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();

    public string FormattedSize => FileSizeBytes switch
    {
        null or 0 => string.Empty,
        < 1024 => $"{FileSizeBytes} B",
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:F1} KB",
        _ => $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB"
    };

    public bool IsImage =>
        !string.IsNullOrWhiteSpace(MimeType)
            ? MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            : IsImageExtension(Extension);

    /// <summary>
    /// Parses a legacy import <c>ATTACHMENT</c>/<c>FILESIZE</c> pair into a member-gated download link.
    /// </summary>
    public static IReadOnlyList<ForumPostAttachment>? Parse(
        string? attachment,
        string? filesize,
        int legacyPostId)
    {
        if (string.IsNullOrWhiteSpace(attachment))
        {
            return null;
        }

        long? bytes = long.TryParse(filesize?.Trim(), out var parsed) ? parsed : null;
        var fileName = attachment.Trim();
        return
        [
            new ForumPostAttachment(
                fileName,
                bytes,
                ForumAttachmentPaths.LegacyDownloadPath(legacyPostId))
        ];
    }

    public static ForumPostAttachment FromStored(StoredForumAttachment stored)
    {
        string? thumb = null;
        if (stored.IsImage)
        {
            // Form attachments upload the original image only. Use that blob for the
            // preview; asking the UGC proxy for a derivative returns 404.
            thumb = $"/ugc/forum/{stored.BlobPath.TrimStart('/')}";
        }

        return new ForumPostAttachment(
            stored.OriginalFileName,
            stored.FileSizeBytes,
            stored.DownloadPath,
            stored.MimeType,
            thumb);
    }

    private static bool IsImageExtension(string extension) =>
        extension is "JPG" or "JPEG" or "PNG" or "GIF" or "WEBP" or "BMP";
}
