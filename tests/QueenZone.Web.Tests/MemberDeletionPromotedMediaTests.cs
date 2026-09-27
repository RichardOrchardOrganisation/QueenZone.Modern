using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class MemberDeletionPromotedMediaTests
{
    [Fact]
    public void EnqueueGalleryLegacyPaths_ResolvesFolderAndFile()
    {
        var blobs = new List<MemberDeletionBlob>();
        var memberId = Guid.NewGuid();

        MemberDeletionPromotedMedia.EnqueueGalleryLegacyPaths(
            memberId,
            "/Brian_May/live.jpg",
            "/Brian_May/live-thumb.webp",
            blobs);

        Assert.Equal(2, blobs.Count);
        Assert.All(blobs, blob => Assert.Equal(memberId, blob.MemberAccountId));
        Assert.Contains(blobs, blob => blob is { Container: "brian-may", Path: "live.jpg" });
        Assert.Contains(blobs, blob => blob is { Container: "brian-may", Path: "live-thumb.webp" });
    }

    [Fact]
    public void EnqueueGalleryLegacyPaths_SkipsUnresolvedPaths()
    {
        var blobs = new List<MemberDeletionBlob>();

        MemberDeletionPromotedMedia.EnqueueGalleryLegacyPaths(
            Guid.NewGuid(),
            "not-a-legacy-path",
            "   ",
            blobs);

        Assert.Empty(blobs);
    }

    [Fact]
    public void EnqueueStageAudio_QueuesSafeSongfilesBlob()
    {
        var blobs = new List<MemberDeletionBlob>();
        var memberId = Guid.NewGuid();

        MemberDeletionPromotedMedia.EnqueueStageAudio(memberId, "cover.mp3", blobs);

        var queued = Assert.Single(blobs);
        Assert.Equal(memberId, queued.MemberAccountId);
        Assert.Equal(SongFileUrl.ContainerName, queued.Container);
        Assert.Equal("cover.mp3", queued.Path);
    }

    [Fact]
    public void EnqueueStageAudio_SkipsUnsafeFileNames()
    {
        var blobs = new List<MemberDeletionBlob>();

        MemberDeletionPromotedMedia.EnqueueStageAudio(Guid.NewGuid(), "../secret.mp3", blobs);
        MemberDeletionPromotedMedia.EnqueueStageAudio(Guid.NewGuid(), "folder/file.mp3", blobs);
        MemberDeletionPromotedMedia.EnqueueStageAudio(Guid.NewGuid(), " ", blobs);

        Assert.Empty(blobs);
    }
}
