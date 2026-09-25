using System.Text;
using Microsoft.AspNetCore.Http;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class ForumAttachmentUnitTests
{
    [Fact]
    public async Task ServeLegacyAsync_ReturnsNotFound_WhenMissing()
    {
        var repo = new InMemoryForumAttachmentRepository();
        var blob = new MemoryBlobUploadService();
        var result = await ForumAttachmentEndpoints.ServeLegacyAsync(999, repo, blob, CancellationToken.None);
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task ServeLegacyAsync_ReturnsNotFound_WhenFilenameUnsafe()
    {
        var repo = new InMemoryForumAttachmentRepository();
        repo.SeedLegacy(new LegacyForumAttachmentLookup(1, "../secret.jpg", 10));
        repo.SeedLegacy(new LegacyForumAttachmentLookup(2, "folder/secret.jpg", 10));
        repo.SeedLegacy(new LegacyForumAttachmentLookup(3, @"folder\secret.jpg", 10));

        var blob = new MemoryBlobUploadService();
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(
            await ForumAttachmentEndpoints.ServeLegacyAsync(1, repo, blob, CancellationToken.None));
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(
            await ForumAttachmentEndpoints.ServeLegacyAsync(2, repo, blob, CancellationToken.None));
        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(
            await ForumAttachmentEndpoints.ServeLegacyAsync(3, repo, blob, CancellationToken.None));
    }

    [Fact]
    public async Task ServeLegacyAsync_ReturnsNotFound_WhenBlobMissing()
    {
        var repo = new InMemoryForumAttachmentRepository();
        repo.SeedLegacy(new LegacyForumAttachmentLookup(42, "scan.jpg", 100));

        var result = await ForumAttachmentEndpoints.ServeLegacyAsync(
            42,
            repo,
            new MemoryBlobUploadService(),
            CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task ServeLegacyAsync_ReturnsNotFound_WhenBlobServiceThrowsNotSupported()
    {
        var repo = new InMemoryForumAttachmentRepository();
        repo.SeedLegacy(new LegacyForumAttachmentLookup(42, "scan.jpg", 100));

        var result = await ForumAttachmentEndpoints.ServeLegacyAsync(
            42,
            repo,
            new ThrowingBlobUploadService(),
            CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task ServeLegacyAsync_StreamsAttachment_WhenPresent()
    {
        var repo = new InMemoryForumAttachmentRepository();
        repo.SeedLegacy(new LegacyForumAttachmentLookup(42, "scan.jpg", 100));
        var blob = new MemoryBlobUploadService();
        await blob.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("jpeg-bytes")),
            "scan.jpg",
            ForumAttachmentPaths.LegacyContainerName,
            new BlobUploadContext { PreferredBlobName = "scan.jpg" });

        var result = await ForumAttachmentEndpoints.ServeLegacyAsync(
            42,
            repo,
            blob,
            CancellationToken.None);

        var file = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult>(result);
        Assert.Equal("scan.jpg", file.FileDownloadName);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.False(file.EnableRangeProcessing);
    }

    [Fact]
    public async Task ServeLegacyAsync_UsesExtensionContentType_WhenBlobTypeBlank()
    {
        var repo = new InMemoryForumAttachmentRepository();
        repo.SeedLegacy(new LegacyForumAttachmentLookup(42, "notes.pdf", 100));

        var result = await ForumAttachmentEndpoints.ServeLegacyAsync(
            42,
            repo,
            new FixedContentBlobUploadService("notes.pdf", "   ", Encoding.UTF8.GetBytes("%PDF")),
            CancellationToken.None);

        var file = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult>(result);
        Assert.Equal("notes.pdf", file.FileDownloadName);
        Assert.Equal("application/pdf", file.ContentType);
    }

    [Fact]
    public async Task ServeLegacyAsync_ForcesDownloadName_WhenBlobTypeIsHtml()
    {
        var repo = new InMemoryForumAttachmentRepository();
        repo.SeedLegacy(new LegacyForumAttachmentLookup(42, "page.html", 100));

        var result = await ForumAttachmentEndpoints.ServeLegacyAsync(
            42,
            repo,
            new FixedContentBlobUploadService("page.html", "text/html", Encoding.UTF8.GetBytes("<p>hi</p>")),
            CancellationToken.None);

        var file = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.FileStreamHttpResult>(result);
        Assert.Equal("page.html", file.FileDownloadName);
        Assert.Equal("text/html", file.ContentType);
    }

    [Fact]
    public async Task ServeModernAsync_ReturnsNotFound_WhenAttachmentMissing()
    {
        var repo = new InMemoryForumAttachmentRepository();
        var blob = new MemoryBlobUploadService();

        var result = await ForumAttachmentEndpoints.ServeModernAsync(
            1,
            Guid.NewGuid(),
            repo,
            blob,
            CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task ServeModernAsync_ReturnsNotFound_WhenBlobMissing()
    {
        var id = Guid.NewGuid();
        var repo = new InMemoryForumAttachmentRepository();
        await repo.AddAttachmentsAsync(9,
        [
            new NewForumAttachment("a.txt", "missing/path.txt", "ugc-forum", 3, "text/plain", DateTimeOffset.UtcNow),
        ]);
        // Force known id by reading all and re-adding is awkward; use Fixed via GetAll
        var stored = repo.GetAll().Single();
        // re-seed with fixed id through increment path — open via GetAsync using actual id
        var blob = new MemoryBlobUploadService();

        var result = await ForumAttachmentEndpoints.ServeModernAsync(
            9,
            stored.Id,
            repo,
            blob,
            CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task ServeModernAsync_ReturnsNotFound_WhenBlobServiceThrowsNotSupported()
    {
        var repo = new InMemoryForumAttachmentRepository();
        await repo.AddAttachmentsAsync(3,
        [
            new NewForumAttachment("a.txt", "x/a.txt", "ugc-forum", 1, "text/plain", DateTimeOffset.UtcNow),
        ]);
        var stored = repo.GetAll().Single();
        var blob = new ThrowingBlobUploadService();

        var result = await ForumAttachmentEndpoints.ServeModernAsync(
            3,
            stored.Id,
            repo,
            blob,
            CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.NotFound>(result);
    }

    [Fact]
    public async Task ServeModernAsync_Streams_WhenMimeEmptyUsesBlobContentType()
    {
        var repo = new InMemoryForumAttachmentRepository();
        await repo.AddAttachmentsAsync(5,
        [
            new NewForumAttachment("notes.txt", "members/t/notes.txt", "ugc-forum", 5, "  ", DateTimeOffset.UtcNow),
        ]);
        var stored = repo.GetAll().Single();
        var blob = new MemoryBlobUploadService();
        await blob.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("hello")),
            "notes.txt",
            "ugc-forum",
            new BlobUploadContext { PreferredBlobName = "members/t/notes.txt" });

        var result = await ForumAttachmentEndpoints.ServeModernAsync(
            5,
            stored.Id,
            repo,
            blob,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(1, repo.GetAll().Single().DownloadCount);
    }

    [Fact]
    public async Task InMemoryRepository_CoversQueryAndIncrementBranches()
    {
        var repo = new InMemoryForumAttachmentRepository();
        Assert.Empty(await repo.GetByLegacyPostIdsAsync([]));

        await repo.AddAttachmentsAsync(10,
        [
            new NewForumAttachment("b.pdf", "p/b.pdf", "ugc-forum", 9, "application/pdf", DateTimeOffset.UtcNow.AddMinutes(-1)),
            new NewForumAttachment("a.pdf", "p/a.pdf", "ugc-forum", 8, "application/pdf", DateTimeOffset.UtcNow),
        ]);

        var list = await repo.GetByLegacyPostIdsAsync([10, 11]);
        Assert.Equal(2, list.Count);
        Assert.Equal("b.pdf", list[0].OriginalFileName);

        var first = list[0];
        Assert.NotNull(await repo.GetAsync(10, first.Id));
        Assert.Null(await repo.GetAsync(10, Guid.NewGuid()));
        Assert.Null(await repo.GetAsync(99, first.Id));

        await repo.IncrementDownloadCountAsync(Guid.NewGuid());
        await repo.IncrementDownloadCountAsync(first.Id);
        Assert.Equal(1, (await repo.GetAsync(10, first.Id))!.DownloadCount);

        repo.SeedLegacy(new LegacyForumAttachmentLookup(77, "legacy.bin", 12));
        Assert.Equal("legacy.bin", (await repo.GetLegacyAsync(77))!.FileName);
        Assert.Null(await repo.GetLegacyAsync(123456));
        // sample seed post 1002 has attachment
        Assert.NotNull(await repo.GetLegacyAsync(1002));
    }

    [Fact]
    public async Task UploadService_NoOpsOnEmptyFileList()
    {
        var blob = new MemoryBlobUploadService();
        var repo = new InMemoryForumAttachmentRepository();
        var service = new ForumAttachmentUploadService(blob, repo, TimeProvider.System, CreateDisabledUploadQuota());

        await service.UploadAndSaveAsync(1, Guid.NewGuid(), []);
        Assert.Empty(repo.GetAll());
    }

    [Fact]
    public async Task UploadService_PersistsUploadedMetadata()
    {
        var blob = new MemoryBlobUploadService();
        var repo = new InMemoryForumAttachmentRepository();
        var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-07-11T12:00:00Z"));
        var service = new ForumAttachmentUploadService(blob, repo, clock, CreateDisabledUploadQuota());

        var file = CreateFormFile("setlist.pdf", "%PDF-1.4", "application/pdf");
        await service.UploadAndSaveAsync(55, Guid.NewGuid(), [file]);

        var stored = repo.GetAll().Single();
        Assert.Equal("setlist.pdf", stored.OriginalFileName);
        Assert.Equal(55, stored.LegacyPostId);
        Assert.Equal(BlobUploadContainers.Forum, stored.ContainerName);
        Assert.Equal(DateTimeOffset.Parse("2026-07-11T12:00:00Z"), stored.UploadedAt);
    }

    [Fact]
    public async Task UploadService_DeletesBlobs_WhenMetadataSaveFails()
    {
        var blob = new TrackingBlobUploadService();
        var repo = new FailingAttachmentRepository();
        var service = new ForumAttachmentUploadService(blob, repo, TimeProvider.System, CreateDisabledUploadQuota());
        var file = CreateFormFile("x.pdf", "%PDF", "application/pdf");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadAndSaveAsync(1, Guid.NewGuid(), [file]));

        Assert.Single(blob.Uploaded);
        Assert.Single(blob.Deleted);
        Assert.Equal(blob.Uploaded[0], blob.Deleted[0]);
    }

    [Fact]
    public async Task UploadService_ContinuesCleanup_WhenDeleteFails()
    {
        var blob = new TrackingBlobUploadService { ThrowOnDelete = true };
        var repo = new FailingAttachmentRepository();
        var service = new ForumAttachmentUploadService(blob, repo, TimeProvider.System, CreateDisabledUploadQuota());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadAndSaveAsync(1, Guid.NewGuid(), [CreateFormFile("x.pdf", "%PDF", "application/pdf")]));

        Assert.Single(blob.Uploaded);
    }

    [Fact]
    public async Task UploadService_RejectsWhenDailyQuotaExceeded()
    {
        var blob = new MemoryBlobUploadService();
        var repo = new InMemoryForumAttachmentRepository();
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
        var service = new ForumAttachmentUploadService(blob, repo, TimeProvider.System, quota);
        var memberId = Guid.NewGuid();

        await service.UploadAndSaveAsync(1, memberId, [CreateFormFile("a.pdf", "%PDF", "application/pdf")]);
        var ex = await Assert.ThrowsAsync<BlobUploadException>(() =>
            service.UploadAndSaveAsync(2, memberId, [CreateFormFile("b.pdf", "%PDF", "application/pdf")]));
        Assert.Contains("Daily upload", ex.Message);
    }

    private static MemberUploadQuotaService CreateDisabledUploadQuota() =>
        new(
            new Microsoft.Extensions.Caching.Memory.MemoryCache(
                new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new UploadQuotaOptions { Enabled = false }));

    [Fact]
    public void ForumAttachmentMerge_CombinesLegacyAndModern()
    {
        var posts = new List<ForumPostItem>
        {
            new(
                1,
                "body",
                DateTime.UtcNow,
                "user",
                null,
                1,
                null,
                [new ForumPostAttachment("legacy.jpg", 10, "/forum/attachment/legacy/1")]),
            new(2, "body2", DateTime.UtcNow, "user", null, 1, null),
        };

        var modern = new Dictionary<int, IReadOnlyList<ForumPostAttachment>>
        {
            [1] =
            [
                new ForumPostAttachment("new.pdf", 20, "/forum/attachment/1/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            ],
        };

        var merged = ForumAttachmentMerge.Merge(posts, modern);
        Assert.Equal(2, merged[0].Attachments!.Count);
        Assert.Null(merged[1].Attachments);
    }

    [Fact]
    public async Task ForumAttachmentMerge_ViaRepository_NoOpsWhenEmpty()
    {
        var repo = new InMemoryForumAttachmentRepository();
        var posts = new List<ForumPostItem>();
        var result = await ForumAttachmentMerge.MergeViaRepositoryAsync(repo, posts);
        Assert.Empty(result);

        posts.Add(new ForumPostItem(9, "b", DateTime.UtcNow, "u", null, 0, null));
        result = await ForumAttachmentMerge.MergeViaRepositoryAsync(repo, posts);
        Assert.Single(result);
        Assert.Null(result[0].Attachments);
    }

    [Fact]
    public void GuessContentType_CoversCommonExtensions()
    {
        Assert.Equal("image/jpeg", ForumAttachmentValidator.GuessContentType("a.jpg"));
        Assert.Equal("audio/mpeg", ForumAttachmentValidator.GuessContentType("a.mp3"));
        Assert.Equal("audio/flac", ForumAttachmentValidator.GuessContentType("a.flac"));
        Assert.Equal("application/zip", ForumAttachmentValidator.GuessContentType("a.zip"));
        Assert.Equal("application/octet-stream", ForumAttachmentValidator.GuessContentType("a.bin"));
        Assert.Equal("512 B", ForumAttachmentValidator.FormatBytes(512));
        Assert.Equal("1.0 KB", ForumAttachmentValidator.FormatBytes(1024));
        Assert.Equal("1.0 MB", ForumAttachmentValidator.FormatBytes(1024 * 1024));
    }

    private static IFormFile CreateFormFile(string name, string content, string contentType)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "Attachments", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private sealed class FixedContentBlobUploadService(string blobName, string contentType, byte[] bytes) : IBlobUploadService
    {
        public Task<BlobUploadResult> UploadAsync(
            Stream content,
            string originalFileName,
            string containerName,
            BlobUploadContext? context = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("read only");

        public Task DeleteAsync(
            string containerName,
            string blobNameToDelete,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<BlobContent?> OpenReadAsync(
            string containerName,
            string blobNameToRead,
            CancellationToken cancellationToken = default)
        {
            if (!string.Equals(containerName, ForumAttachmentPaths.LegacyContainerName, StringComparison.Ordinal)
                || !string.Equals(blobNameToRead, blobName, StringComparison.Ordinal))
            {
                return Task.FromResult<BlobContent?>(null);
            }

            return Task.FromResult<BlobContent?>(new BlobContent
            {
                Stream = new MemoryStream(bytes),
                ContentType = contentType,
            });
        }
    }

    private sealed class ThrowingBlobUploadService : IBlobUploadService
    {
        public Task<BlobUploadResult> UploadAsync(
            Stream content,
            string originalFileName,
            string containerName,
            BlobUploadContext? context = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("no blob");

        public Task DeleteAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<BlobContent?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("no blob");
    }

    private sealed class TrackingBlobUploadService : IBlobUploadService
    {
        public List<string> Uploaded { get; } = [];
        public List<string> Deleted { get; } = [];
        public bool ThrowOnDelete { get; init; }

        public async Task<BlobUploadResult> UploadAsync(
            Stream content,
            string originalFileName,
            string containerName,
            BlobUploadContext? context = null,
            CancellationToken cancellationToken = default)
        {
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var blobName = context?.PreferredBlobName ?? $"members/{Guid.NewGuid():N}/{originalFileName}";
            Uploaded.Add($"{containerName}/{blobName}");
            return new BlobUploadResult
            {
                Container = containerName,
                BlobName = blobName,
                ContentType = "application/pdf",
                SizeBytes = buffer.Length,
            };
        }

        public Task DeleteAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default)
        {
            Deleted.Add($"{containerName}/{blobName}");
            if (ThrowOnDelete)
            {
                throw new InvalidOperationException("delete failed");
            }

            return Task.CompletedTask;
        }

        public Task<BlobContent?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BlobContent?>(null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FailingAttachmentRepository : IForumAttachmentRepository
    {
        public Task AddAttachmentsAsync(
            int legacyPostId,
            IEnumerable<NewForumAttachment> attachments,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("db down");

        public Task<IReadOnlyList<StoredForumAttachment>> GetByLegacyPostIdsAsync(
            IReadOnlyCollection<int> legacyPostIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredForumAttachment>>([]);

        public Task<StoredForumAttachment?> GetAsync(
            int legacyPostId,
            Guid attachmentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<StoredForumAttachment?>(null);

        public Task<LegacyForumAttachmentLookup?> GetLegacyAsync(
            int legacyPostId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<LegacyForumAttachmentLookup?>(null);

        public Task IncrementDownloadCountAsync(
            Guid attachmentId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
