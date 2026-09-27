using QueenZone.Storage;

namespace QueenZone.Storage.Tests;

public sealed class InMemoryBlobStorageBackendTests
{
    [Fact]
    public async Task OpenReadAsync_exposes_opaque_etag_and_content_length()
    {
        var backend = new InMemoryBlobStorageBackend();
        var payload = "ID3fake-audio"u8.ToArray();
        await using var upload = new MemoryStream(payload);
        await backend.UploadAsync("songfiles", "clip.mp3", upload, "audio/mpeg");

        await using var content = await backend.OpenReadAsync("songfiles", "clip.mp3");

        Assert.NotNull(content);
        Assert.Equal("audio/mpeg", content!.ContentType);
        Assert.Equal(payload.LongLength, content.ContentLength);
        Assert.Equal(InMemoryBlobStorageBackend.ComputeETag(payload), content.ETag);
        Assert.StartsWith("\"", content.ETag);
        Assert.EndsWith("\"", content.ETag);
        Assert.DoesNotContain("songfiles", content.ETag, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("clip.mp3", content.ETag, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Clear_removes_all_uploaded_blobs()
    {
        var backend = new InMemoryBlobStorageBackend();
        await using var upload = new MemoryStream("payload"u8.ToArray());
        await backend.UploadAsync("ugc-avatars", "probe.bin", upload, "application/octet-stream");
        Assert.True(backend.Exists("ugc-avatars", "probe.bin"));

        backend.Clear();

        Assert.False(backend.Exists("ugc-avatars", "probe.bin"));
        Assert.Null(await backend.OpenReadAsync("ugc-avatars", "probe.bin"));
    }
}
