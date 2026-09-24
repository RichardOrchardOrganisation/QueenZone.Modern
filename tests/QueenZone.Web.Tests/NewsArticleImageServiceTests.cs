using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;
using QueenZone.Web.Pages.Admin.News;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace QueenZone.Web.Tests;

public sealed class NewsArticleImageServiceTests
{
    [Fact]
    public async Task TryApplyAsync_uploads_full_and_thumb_to_ugc_articles()
    {
        var service = CreateService(out var blobs, out var backend);
        var draft = new AdminNewsDraft("Title", null, "Excerpt", "Body", DateTime.UtcNow.Date, null);
        await using var png = await CreatePngAsync(600, 400);

        var result = await service.TryApplyAsync(
            CreateFormFile(png, "hero.png"),
            crop: null,
            draft,
            CreateUser(),
            persist: true);

        Assert.Null(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Draft.ImageBlobKey));
        Assert.Null(result.Draft.ImageGalleryPicId);
        Assert.True(backend.Exists(BlobUploadContainers.Articles, result.Draft.ImageBlobKey!));
        Assert.True(backend.Exists(
            BlobUploadContainers.Articles,
            UgcProxyPaths.ToThumbBlobName(result.Draft.ImageBlobKey!)));
        Assert.NotNull(await blobs.OpenReadAsync(BlobUploadContainers.Articles, result.Draft.ImageBlobKey!));
    }

    [Fact]
    public async Task TryApplyAsync_replacing_uploaded_image_keeps_old_blobs_until_caller_deletes()
    {
        var service = CreateService(out _, out var backend);
        var user = CreateUser();
        var draft = new AdminNewsDraft("Title", null, "Excerpt", "Body", DateTime.UtcNow.Date, null);
        await using var first = await CreatePngAsync(600, 400);
        var created = await service.TryApplyAsync(
            CreateFormFile(first, "first.png"),
            null,
            draft,
            user,
            persist: true);
        var previous = created.Draft.ImageBlobKey!;
        await using var second = await CreatePngAsync(640, 420);

        var replaced = await service.TryApplyAsync(
            CreateFormFile(second, "second.png"),
            null,
            created.Draft,
            user,
            persist: true);

        Assert.Null(replaced.Error);
        Assert.NotEqual(previous, replaced.Draft.ImageBlobKey);
        Assert.True(backend.Exists(BlobUploadContainers.Articles, previous));
        Assert.True(backend.Exists(BlobUploadContainers.Articles, UgcProxyPaths.ToThumbBlobName(previous)));
        Assert.True(backend.Exists(BlobUploadContainers.Articles, replaced.Draft.ImageBlobKey!));

        await service.TryDeletePreviousUgcArticlesAsync(previous, replaced.Draft.ImageBlobKey);

        Assert.False(backend.Exists(BlobUploadContainers.Articles, previous));
        Assert.False(backend.Exists(BlobUploadContainers.Articles, UgcProxyPaths.ToThumbBlobName(previous)));
        Assert.True(backend.Exists(BlobUploadContainers.Articles, replaced.Draft.ImageBlobKey!));
    }

    [Fact]
    public async Task Replacing_gallery_reference_does_not_delete_gallery_or_pic_paths()
    {
        var blobs = new RecordingBlobUploadService();
        var service = new NewsArticleImageService(
            blobs,
            CreateDisabledQuota(),
            CreatePhotos(),
            new NullGalleryPhotoBlobService());
        var draft = new AdminNewsDraft(
            "Title",
            null,
            "Excerpt",
            "Body",
            DateTime.UtcNow.Date,
            null,
            "gallery:3120",
            3120);
        await using var png = await CreatePngAsync(600, 400);

        var result = await service.TryApplyAsync(
            CreateFormFile(png, "hero.png"),
            null,
            draft,
            CreateUser(),
            persist: true);

        await service.TryDeletePreviousUgcArticlesAsync("gallery:3120", result.Draft.ImageBlobKey);

        Assert.Null(result.Error);
        Assert.DoesNotContain(
            blobs.Deleted,
            item => item.BlobName.Contains("gallery", StringComparison.OrdinalIgnoreCase)
                || item.BlobName.Contains("3120", StringComparison.Ordinal));
        Assert.All(blobs.Deleted, item => Assert.Equal(BlobUploadContainers.Articles, item.Container));
    }

    [Fact]
    public async Task TryApplyAsync_without_persist_validates_but_does_not_upload()
    {
        var service = CreateService(out _, out var backend);
        var draft = new AdminNewsDraft("Title", null, "Excerpt", "Body", DateTime.UtcNow.Date, null);
        await using var png = await CreatePngAsync(600, 400);

        var result = await service.TryApplyAsync(
            CreateFormFile(png, "hero.png"),
            null,
            draft,
            CreateUser(),
            persist: false);

        Assert.Null(result.Error);
        Assert.Null(result.Draft.ImageBlobKey);
        Assert.False(backend.Exists(BlobUploadContainers.Articles, "editors/editor-test.local"));
    }

    [Fact]
    public async Task TryApplyAsync_returns_error_for_unsupported_type()
    {
        var service = CreateService(out _, out _);
        var draft = new AdminNewsDraft("Title", null, "Excerpt", "Body", DateTime.UtcNow.Date, null);
        await using var source = new MemoryStream("not-an-image"u8.ToArray());

        var result = await service.TryApplyAsync(
            CreateFormFile(source, "note.txt", "text/plain"),
            null,
            draft,
            CreateUser(),
            persist: true);

        Assert.Contains("JPEG, PNG, or WebP", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Draft.ImageBlobKey);
    }

    [Fact]
    public async Task TryApplyAsync_gallery_pick_with_crop_copies_to_ugc_articles_and_nulls_pic_id()
    {
        var gallery = await SeededRecordingGalleryAsync(101, 900, 600);
        var originalBefore = await ReadGalleryBytesAsync(gallery, 101);
        var service = new NewsArticleImageService(
            CreateBlobs(out var backend),
            CreateDisabledQuota(),
            CreatePhotos(),
            gallery);
        var draft = NewsArticleImage.WithGalleryPick(
            new AdminNewsDraft("Title", null, "Excerpt", "Body", DateTime.UtcNow.Date, null),
            101);

        var result = await service.TryApplyAsync(
            file: null,
            new NewsArticleImageCrop(150, 0, 600, 400),
            draft,
            CreateUser(),
            persist: true);

        Assert.Null(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Draft.ImageBlobKey));
        Assert.False(NewsArticleImage.IsGalleryReference(result.Draft.ImageBlobKey));
        Assert.Null(result.Draft.ImageGalleryPicId);
        Assert.True(backend.Exists(BlobUploadContainers.Articles, result.Draft.ImageBlobKey!));
        Assert.Empty(gallery.Uploads);
        Assert.Empty(gallery.Deletes);
        Assert.Equal(originalBefore, await ReadGalleryBytesAsync(gallery, 101));
    }

    [Fact]
    public async Task TryApplyAsync_new_gallery_pick_without_crop_is_rejected()
    {
        var service = CreateService(out _, out _);
        var draft = NewsArticleImage.WithGalleryPick(
            new AdminNewsDraft("Title", null, "Excerpt", "Body", DateTime.UtcNow.Date, null),
            101);

        var result = await service.TryApplyAsync(file: null, crop: null, draft, CreateUser(), persist: true);

        Assert.Contains("Apply a 3:2 crop", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("gallery:101", result.Draft.ImageBlobKey);
        Assert.Equal(101, result.Draft.ImageGalleryPicId);
    }

    [Fact]
    public async Task TryApplyAsync_unchanged_persisted_gallery_pointer_skips_copy()
    {
        var gallery = new RecordingGalleryPhotoBlobService(new NullGalleryPhotoBlobService());
        var service = new NewsArticleImageService(
            CreateBlobs(out var backend),
            CreateDisabledQuota(),
            CreatePhotos(),
            gallery);
        var draft = NewsArticleImage.WithGalleryPick(
            new AdminNewsDraft("Title", null, "Excerpt", "Body", DateTime.UtcNow.Date, null),
            101);

        var result = await service.TryApplyAsync(
            file: null,
            crop: null,
            draft,
            CreateUser(),
            persist: true,
            persistedGalleryPicId: 101);

        Assert.Null(result.Error);
        Assert.Equal("gallery:101", result.Draft.ImageBlobKey);
        Assert.Equal(101, result.Draft.ImageGalleryPicId);
        Assert.Empty(gallery.Uploads);
        Assert.Empty(gallery.Deletes);
        Assert.False(backend.Exists(BlobUploadContainers.Articles, "editors/editor-test.local"));
    }

    [Fact]
    public async Task TryApplyAsync_invalid_gallery_crop_is_rejected()
    {
        var gallery = await SeededRecordingGalleryAsync(101, 900, 600);
        var service = new NewsArticleImageService(
            CreateBlobs(out _),
            CreateDisabledQuota(),
            CreatePhotos(),
            gallery);
        var draft = NewsArticleImage.WithGalleryPick(
            new AdminNewsDraft("Title", null, "Excerpt", "Body", DateTime.UtcNow.Date, null),
            101);

        var result = await service.TryApplyAsync(
            file: null,
            new NewsArticleImageCrop(0, 0, 100, 100),
            draft,
            CreateUser(),
            persist: true);

        Assert.Contains("invalid", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("gallery:101", result.Draft.ImageBlobKey);
        Assert.Empty(gallery.Uploads);
        Assert.Empty(gallery.Deletes);
    }

    [Fact]
    public async Task TryApplyAsync_returns_error_when_blob_upload_fails()
    {
        var service = new NewsArticleImageService(
            new ThrowingBlobUploadService(),
            CreateDisabledQuota(),
            CreatePhotos(),
            new NullGalleryPhotoBlobService());
        var draft = new AdminNewsDraft("Title", null, "Excerpt", "Body", DateTime.UtcNow.Date, null);
        await using var png = await CreatePngAsync(600, 400);

        var result = await service.TryApplyAsync(
            CreateFormFile(png, "hero.png"),
            null,
            draft,
            CreateUser(),
            persist: true);

        Assert.Equal("upload failed", result.Error);
        Assert.Null(result.Draft.ImageBlobKey);
    }

    private static NewsArticleImageService CreateService(
        out IBlobUploadService blobs,
        out InMemoryBlobStorageBackend backend)
    {
        blobs = CreateBlobs(out backend);
        return new NewsArticleImageService(blobs, CreateDisabledQuota(), CreatePhotos(), new NullGalleryPhotoBlobService());
    }

    private static IBlobUploadService CreateBlobs(out InMemoryBlobStorageBackend backend)
    {
        backend = new InMemoryBlobStorageBackend();
        return new AzureBlobUploadService(
            backend,
            Microsoft.Extensions.Options.Options.Create(new BlobUploadOptions()));
    }

    private static IAdminPhotoRepository CreatePhotos() =>
        new InMemoryAdminPhotoRepository(new SharedPhotoStore(SamplePhotoData.CreateSeedCategories()));

    private static async Task<RecordingGalleryPhotoBlobService> SeededRecordingGalleryAsync(
        int picId,
        int width,
        int height)
    {
        var inner = new NullGalleryPhotoBlobService();
        var photos = CreatePhotos();
        var photo = await photos.GetByIdAsync(picId);
        Assert.NotNull(photo);
        Assert.True(NewsArticleGalleryPicker.TryResolveBlobLocation(photo.LegacyUrl, out var container, out var blobName));
        using var image = new Image<Rgba32>(width, height);
        await using var stream = new MemoryStream();
        await image.SaveAsync(stream, new JpegEncoder());
        stream.Position = 0;
        await inner.UploadAsync(container, blobName, stream, "image/jpeg");
        return new RecordingGalleryPhotoBlobService(inner);
    }

    private static async Task<byte[]> ReadGalleryBytesAsync(IGalleryPhotoBlobService gallery, int picId)
    {
        var photos = CreatePhotos();
        var photo = await photos.GetByIdAsync(picId);
        Assert.NotNull(photo);
        Assert.True(NewsArticleGalleryPicker.TryResolveBlobLocation(photo.LegacyUrl, out var container, out var blobName));
        await using var stream = await gallery.OpenReadAsync(container, blobName);
        Assert.NotNull(stream);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private static MemberUploadQuotaService CreateDisabledQuota() =>
        new(
            new MemoryCache(new MemoryCacheOptions()),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new UploadQuotaOptions { Enabled = false }));

    private static ClaimsPrincipal CreateUser() =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "editor@test.local")],
            authenticationType: "test"));

    private static IFormFile CreateFormFile(Stream stream, string fileName, string contentType = "image/png")
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return new FormFile(stream, 0, stream.Length, "articleImage", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private static async Task<MemoryStream> CreatePngAsync(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder());
        stream.Position = 0;
        return stream;
    }

    private sealed class RecordingBlobUploadService : IBlobUploadService
    {
        public List<(string Container, string BlobName)> Deleted { get; } = [];

        private int nextId = 1;

        public Task<BlobUploadResult> UploadAsync(
            Stream content,
            string originalFileName,
            string containerName,
            BlobUploadContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var name = context?.PreferredBlobName ?? $"editors/me/{nextId++}.webp";
            _ = content;
            _ = originalFileName;
            return Task.FromResult(new BlobUploadResult
            {
                Container = containerName,
                BlobName = name,
                ContentType = "image/webp",
                SizeBytes = 12,
            });
        }

        public Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
        {
            Deleted.Add((containerName, blobName));
            return Task.CompletedTask;
        }

        public Task<BlobContent?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BlobContent?>(null);
    }

    private sealed class RecordingGalleryPhotoBlobService(IGalleryPhotoBlobService inner) : IGalleryPhotoBlobService
    {
        public List<string> Uploads { get; } = [];

        public List<string> Deletes { get; } = [];

        public bool IsConfigured => inner.IsConfigured;

        public Task UploadAsync(
            string containerName,
            string blobName,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            Uploads.Add(containerName + "/" + blobName);
            return inner.UploadAsync(containerName, blobName, content, contentType, cancellationToken);
        }

        public Task<Stream?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            inner.OpenReadAsync(containerName, blobName, cancellationToken);

        public Task DeleteAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default)
        {
            Deletes.Add(containerName + "/" + blobName);
            return inner.DeleteAsync(containerName, blobName, cancellationToken);
        }

        public IAsyncEnumerable<GalleryBlobDescriptor> ListBlobsAsync(
            string containerName,
            CancellationToken cancellationToken = default) =>
            inner.ListBlobsAsync(containerName, cancellationToken);
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
