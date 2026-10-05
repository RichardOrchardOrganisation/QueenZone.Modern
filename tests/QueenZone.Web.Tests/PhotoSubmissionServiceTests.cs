using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class PhotoSubmissionServiceTests
{
    [Fact]
    public async Task SubmitAsync_rejects_empty_member_and_title()
    {
        var service = CreateService(out _);

        await using var png = await CreatePngAsync();
        var emptyMember = await service.SubmitAsync(Guid.Empty, new PhotoSubmissionDetails("Title", null, null, null, null), png, "a.png");
        Assert.False(emptyMember.Succeeded);

        png.Position = 0;
        var emptyTitle = await service.SubmitAsync(Guid.NewGuid(), new PhotoSubmissionDetails("  ", null, null, null, null), png, "a.png");
        Assert.False(emptyTitle.Succeeded);

        png.Position = 0;
        var longTitle = await service.SubmitAsync(Guid.NewGuid(), new PhotoSubmissionDetails(new string('t', 201), null, null, null, null), png, "a.png");
        Assert.False(longTitle.Succeeded);
    }

    [Fact]
    public async Task SubmitAsync_rejects_invalid_image()
    {
        var service = CreateService(out _);
        await using var junk = new MemoryStream("not-image"u8.ToArray());
        var result = await service.SubmitAsync(Guid.NewGuid(), new PhotoSubmissionDetails("Bad file", null, null, null, null), junk, "note.txt");
        Assert.False(result.Succeeded);
        Assert.Contains("JPEG", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_maps_extension_from_mime_when_missing()
    {
        var service = CreateService(out var blobs);
        await using var png = await CreatePngAsync();
        var result = await service.SubmitAsync(Guid.NewGuid(), new PhotoSubmissionDetails("No extension", null, "Queen", 1986, null), png, "noext");
        Assert.True(result.Succeeded);
        Assert.Contains("/original.png", result.Submission!.BlobPath);
        Assert.True(blobs.Exists(BlobUploadContainers.Photos, result.Submission.BlobPath));
        Assert.True(blobs.Exists(BlobUploadContainers.Photos, result.Submission.WebOptimizedBlobPath));
        Assert.True(blobs.Exists(BlobUploadContainers.Photos, result.Submission.ThumbnailBlobPath));
    }

    [Fact]
    public async Task SubmitAsync_records_measured_image_dimensions()
    {
        var service = CreateService(out _);
        await using var png = await CreatePngAsync();
        var result = await service.SubmitAsync(Guid.NewGuid(), new PhotoSubmissionDetails("Dimensions", null, null, null, null), png, "a.png");

        Assert.True(result.Succeeded);
        Assert.Equal(40, result.Submission!.ImageWidthPx);
        Assert.Equal(40, result.Submission.ImageHeightPx);
    }

    [Fact]
    public async Task SubmitAsync_surfaces_blob_upload_errors()
    {
        var repository = new InMemoryPhotoSubmissionRepository();
        var service = new PhotoSubmissionService(
            repository,
            new ThrowingBlobUploadService(),
            CreateDisabledUploadQuota());
        await using var png = await CreatePngAsync();
        var result = await service.SubmitAsync(Guid.NewGuid(), new PhotoSubmissionDetails("Upload fail", null, null, null, null), png, "a.png");
        Assert.False(result.Succeeded);
        Assert.Contains("upload failed", result.Error);
    }

    [Fact]
    public async Task SubmitAsync_rejects_when_daily_quota_exceeded()
    {
        var quota = new MemberUploadQuotaService(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new UploadQuotaOptions
            {
                Enabled = true,
                MaxUploadsPerDay = 1,
                MaxBytesPerDay = 100L * 1024 * 1024,
            }));
        var service = new PhotoSubmissionService(
            new InMemoryPhotoSubmissionRepository(),
            new AzureBlobUploadService(
                new InMemoryBlobStorageBackend(),
                Microsoft.Extensions.Options.Options.Create(new BlobUploadOptions())),
            quota);

        var memberId = Guid.NewGuid();
        await using var first = await CreatePngAsync();
        var ok = await service.SubmitAsync(memberId, new PhotoSubmissionDetails("One", null, null, null, null), first, "a.png");
        Assert.True(ok.Succeeded);

        await using var second = await CreatePngAsync();
        var blocked = await service.SubmitAsync(memberId, new PhotoSubmissionDetails("Two", null, null, null, null), second, "b.png");
        Assert.False(blocked.Succeeded);
        Assert.Contains("Daily upload", blocked.Error);
    }

    private static PhotoSubmissionService CreateService(out InMemoryBlobStorageBackend backend)
    {
        backend = new InMemoryBlobStorageBackend();
        var blobs = new AzureBlobUploadService(backend, Microsoft.Extensions.Options.Options.Create(new BlobUploadOptions()));
        return new PhotoSubmissionService(new InMemoryPhotoSubmissionRepository(), blobs, CreateDisabledUploadQuota());
    }

    private static MemberUploadQuotaService CreateDisabledUploadQuota() =>
        new(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new UploadQuotaOptions { Enabled = false }));

    private static async Task<MemoryStream> CreatePngAsync()
    {
        using var image = new Image<Rgba32>(40, 40, new Rgba32(10, 20, 30));
        var stream = new MemoryStream();
        await image.SaveAsPngAsync(stream);
        stream.Position = 0;
        return stream;
    }

    private sealed class ThrowingBlobUploadService : IBlobUploadService
    {
        public Task<BlobUploadResult> UploadAsync(
            Stream content,
            string originalFileName,
            string containerName,
            BlobUploadContext? context = null,
            CancellationToken cancellationToken = default) =>
            throw new BlobUploadException("upload failed");

        public Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<BlobContent?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BlobContent?>(null);
    }
}
