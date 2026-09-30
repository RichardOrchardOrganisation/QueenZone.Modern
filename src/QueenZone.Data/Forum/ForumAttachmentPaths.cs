namespace QueenZone.Data;

/// <summary>
/// App-relative download paths for forum attachments.
/// HTML never links straight to public blob hosts; members hit the app, which streams the bytes.
/// </summary>
public static class ForumAttachmentPaths
{
    /// <summary>
    /// Private legacy import container. Readable only through the member-gated app proxy.
    /// </summary>
    public const string LegacyContainerName = "attachments";

    public static string LegacyDownloadPath(int legacyPostId) =>
        $"/forum/attachment/legacy/{legacyPostId}";

    public static string DownloadPath(int legacyPostId, Guid attachmentId) =>
        $"/forum/attachment/{legacyPostId}/{attachmentId:D}";
}
