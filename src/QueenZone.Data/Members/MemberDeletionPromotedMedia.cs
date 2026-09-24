namespace QueenZone.Data;

/// <summary>
/// Resolves promoted gallery and stage blob paths for the account-deletion outbox.
/// Unresolved or unsafe paths are skipped so PII purge is never gated on blob location.
/// </summary>
internal static class MemberDeletionPromotedMedia
{
    public static void EnqueueGalleryLegacyPaths(
        Guid memberId,
        string? legacyUrl,
        string? legacyThumbUrl,
        ICollection<MemberDeletionBlob> blobs)
    {
        TryEnqueueGalleryPath(memberId, legacyUrl, blobs);
        TryEnqueueGalleryPath(memberId, legacyThumbUrl, blobs);
    }

    public static void EnqueueStageAudio(
        Guid memberId,
        string? audioFileName,
        ICollection<MemberDeletionBlob> blobs)
    {
        if (!SongFileUrl.IsSafeBlobName(audioFileName))
        {
            return;
        }

        blobs.Add(new MemberDeletionBlob(
            memberId,
            SongFileUrl.ContainerName,
            SongFileUrl.GetBlobName(audioFileName!)));
    }

    private static void TryEnqueueGalleryPath(
        Guid memberId,
        string? legacyPath,
        ICollection<MemberDeletionBlob> blobs)
    {
        if (string.IsNullOrWhiteSpace(legacyPath))
        {
            return;
        }

        var blobUrl = PhotoImageUrl.ToBlobStorageUrl(legacyPath);
        if (!PhotoImageUrl.TryParseBlobLocation(blobUrl, out var container, out var blobName))
        {
            return;
        }

        blobs.Add(new MemberDeletionBlob(memberId, container, blobName));
    }
}
