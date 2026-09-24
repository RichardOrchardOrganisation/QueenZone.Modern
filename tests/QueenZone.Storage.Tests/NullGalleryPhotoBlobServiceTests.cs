namespace QueenZone.Storage.Tests;

public sealed class NullGalleryPhotoBlobServiceTests
{
    [Fact]
    public async Task ListBlobsAsync_ReturnsUploadedBlobsForContainer_WithLastModified()
    {
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var service = new NullGalleryPhotoBlobService { TimeProvider = new FixedClock(now) };

        await service.UploadAsync("queen", "photo.webp", new MemoryStream([1, 2, 3]), "image/webp");
        await service.UploadAsync("brian-may", "other.webp", new MemoryStream([4, 5, 6]), "image/webp");

        var blobs = await service.ListBlobsAsync("queen").ToListAsync();

        var blob = Assert.Single(blobs);
        Assert.Equal("photo.webp", blob.BlobName);
        Assert.Equal(now, blob.LastModified);
    }

    [Fact]
    public async Task ListBlobsAsync_ReturnsEmpty_ForUnknownContainer()
    {
        var service = new NullGalleryPhotoBlobService();

        var blobs = await service.ListBlobsAsync("does-not-exist").ToListAsync();

        Assert.Empty(blobs);
    }

    [Fact]
    public async Task ListBlobsAsync_OmitsDeletedBlobs()
    {
        var service = new NullGalleryPhotoBlobService();
        await service.UploadAsync("queen", "photo.webp", new MemoryStream([1, 2, 3]), "image/webp");

        await service.DeleteAsync("queen", "photo.webp");

        Assert.Empty(await service.ListBlobsAsync("queen").ToListAsync());
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
