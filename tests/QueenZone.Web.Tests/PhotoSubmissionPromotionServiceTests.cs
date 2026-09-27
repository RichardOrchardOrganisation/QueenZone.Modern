using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class PhotoSubmissionPromotionServiceTests
{
    private readonly Guid memberId = Guid.NewGuid();

    [Fact]
    public async Task PromoteAsync_CopiesBlobsCreatesPublicRowAndMarksSubmissionPromoted()
    {
        var submissionRepository = new InMemoryPhotoSubmissionRepository();
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var publicPhotoRepository = new InMemoryPhotoRepository(store);
        var blobUploadService = new FakeBlobUploadService();
        var galleryPhotoBlobService = new NullGalleryPhotoBlobService();
        var service = CreateService(
            submissionRepository, adminPhotoRepository, blobUploadService, galleryPhotoBlobService);

        var submission = await CreateApprovableSubmissionAsync(submissionRepository, blobUploadService, "Queen");

        var picId = await service.PromoteAsync(submission, "Queen", "admin@test.local", "Looks great");

        var updated = await submissionRepository.GetByIdAsync(submission.Id);
        Assert.Equal(PhotoSubmissionStatus.Approved, updated!.Status);
        Assert.Equal(picId, updated.PromotedPicId);
        Assert.Equal("Queen", updated.ApprovedCategory);

        var publicItem = await adminPhotoRepository.GetByIdAsync(picId);
        Assert.NotNull(publicItem);
        Assert.Equal(12, publicItem!.CatId);
        Assert.True(publicItem.IsVisible);
        Assert.Equal(submission.Title, publicItem.Title);

        var allInCategory = await publicPhotoRepository.GetCategoryAllAsync(12);
        Assert.Contains(allInCategory, item => item.PicId == picId);
    }

    [Fact]
    public async Task PromoteAsync_Throws_WhenCategoryDoesNotExist()
    {
        var submissionRepository = new InMemoryPhotoSubmissionRepository();
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var blobUploadService = new FakeBlobUploadService();
        var service = CreateService(
            submissionRepository, adminPhotoRepository, blobUploadService, new NullGalleryPhotoBlobService());

        var submission = await CreateApprovableSubmissionAsync(submissionRepository, blobUploadService, "Nonexistent");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PromoteAsync(submission, "Nonexistent", "admin@test.local", null));
        Assert.Contains("does not exist", ex.Message);

        var unchanged = await submissionRepository.GetByIdAsync(submission.Id);
        Assert.Equal(PhotoSubmissionStatus.Pending, unchanged!.Status);
        Assert.Null(unchanged.PromotedPicId);
    }

    [Fact]
    public async Task PromoteAsync_Throws_WhenBlobMissing()
    {
        var submissionRepository = new InMemoryPhotoSubmissionRepository();
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var blobUploadService = new FakeBlobUploadService();
        var service = CreateService(
            submissionRepository, adminPhotoRepository, blobUploadService, new NullGalleryPhotoBlobService());

        var created = await submissionRepository.CreateAsync(new NewPhotoSubmission(
            memberId,
            "No blobs",
            "desc",
            "Queen",
            1986,
            new DateOnly(1986, 7, 12),
            "members/x/original.jpg",
            "members/x/display.webp",
            "members/x/thumb.webp",
            "photo.jpg",
            2048,
            "image/jpeg",
            800,
            600));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PromoteAsync(created, "Queen", "admin@test.local", null));
        Assert.Contains("missing from storage", ex.Message);
    }

    [Fact]
    public async Task PromoteAsync_DeletesUploadedBlobs_WhenDbWriteFailsAfterUpload()
    {
        var submissionRepository = new FailingOnPromoteSubmissionRepository();
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var blobUploadService = new FakeBlobUploadService();
        var galleryPhotoBlobService = new RecordingGalleryPhotoBlobService();
        var service = CreateService(
            submissionRepository, adminPhotoRepository, blobUploadService, galleryPhotoBlobService);

        var submission = await CreateApprovableSubmissionAsync(submissionRepository, blobUploadService, "Queen");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PromoteAsync(submission, "Queen", "admin@test.local", "Looks great"));

        Assert.Equal(2, galleryPhotoBlobService.Uploaded.Count);
        Assert.Equal(galleryPhotoBlobService.Uploaded, galleryPhotoBlobService.Deleted);
    }

    [Fact]
    public async Task PromoteAsync_StillThrowsOriginalError_WhenCompensatingDeleteAlsoFails()
    {
        var submissionRepository = new FailingOnPromoteSubmissionRepository();
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var blobUploadService = new FakeBlobUploadService();
        var galleryPhotoBlobService = new RecordingGalleryPhotoBlobService { FailDeletes = true };
        var service = CreateService(
            submissionRepository, adminPhotoRepository, blobUploadService, galleryPhotoBlobService);

        var submission = await CreateApprovableSubmissionAsync(submissionRepository, blobUploadService, "Queen");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PromoteAsync(submission, "Queen", "admin@test.local", "Looks great"));

        Assert.Equal("Simulated DB write failure after blob upload.", ex.Message);
        Assert.Equal(2, galleryPhotoBlobService.Uploaded.Count);
        Assert.Equal(2, galleryPhotoBlobService.FailedDeleteAttempts);
    }

    private static PhotoSubmissionPromotionService CreateService(
        IPhotoSubmissionRepository submissionRepository,
        IAdminPhotoRepository adminPhotoRepository,
        IBlobUploadService blobUploadService,
        IGalleryPhotoBlobService galleryPhotoBlobService) =>
        new(
            submissionRepository,
            adminPhotoRepository,
            blobUploadService,
            galleryPhotoBlobService,
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<PhotoSubmissionPromotionService>.Instance);

    private async Task<PhotoSubmission> CreateApprovableSubmissionAsync(
        IPhotoSubmissionRepository submissionRepository,
        FakeBlobUploadService blobUploadService,
        string suggestedCategory)
    {
        var id = Guid.NewGuid();
        var webPath = $"members/{memberId:N}/{id:N}/display.webp";
        var thumbPath = $"members/{memberId:N}/{id:N}/thumb.webp";

        blobUploadService.Seed(BlobUploadContainers.Photos, webPath, "image/webp"u8.ToArray());
        blobUploadService.Seed(BlobUploadContainers.Photos, thumbPath, "image/webp"u8.ToArray());

        return await submissionRepository.CreateAsync(new NewPhotoSubmission(
            memberId,
            "Promotable photo",
            "desc",
            suggestedCategory,
            1986,
            new DateOnly(1986, 7, 12),
            $"members/{memberId:N}/{id:N}/original.jpg",
            webPath,
            thumbPath,
            "photo.jpg",
            2048,
            "image/jpeg",
            800,
            600,
            id));
    }

    private sealed class FakeBlobUploadService : IBlobUploadService
    {
        private readonly Dictionary<string, byte[]> blobs = new(StringComparer.OrdinalIgnoreCase);

        public void Seed(string containerName, string blobName, byte[] content) =>
            blobs[Key(containerName, blobName)] = content;

        public Task<BlobUploadResult> UploadAsync(
            Stream content,
            string originalFileName,
            string containerName,
            BlobUploadContext? context = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by promotion — promotion only reads existing submission blobs.");

        public Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
        {
            blobs.Remove(Key(containerName, blobName));
            return Task.CompletedTask;
        }

        public Task<BlobContent?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default)
        {
            if (!blobs.TryGetValue(Key(containerName, blobName), out var bytes))
            {
                return Task.FromResult<BlobContent?>(null);
            }

            return Task.FromResult<BlobContent?>(new BlobContent
            {
                Stream = new MemoryStream(bytes, writable: false),
                ContentType = "image/webp",
            });
        }

        private static string Key(string containerName, string blobName) => $"{containerName}/{blobName}";
    }

    private sealed class RecordingGalleryPhotoBlobService : IGalleryPhotoBlobService
    {
        public List<(string Container, string BlobName)> Uploaded { get; } = [];

        public List<(string Container, string BlobName)> Deleted { get; } = [];

        public bool FailDeletes { get; init; }

        public int FailedDeleteAttempts { get; private set; }

        public bool IsConfigured => true;

        public Task UploadAsync(
            string containerName,
            string blobName,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            Uploaded.Add((containerName, blobName));
            return Task.CompletedTask;
        }

        public Task<Stream?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
        {
            if (FailDeletes)
            {
                FailedDeleteAttempts++;
                throw new InvalidOperationException("Simulated blob delete failure.");
            }

            Deleted.Add((containerName, blobName));
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<GalleryBlobDescriptor> ListBlobsAsync(
            string containerName,
            CancellationToken cancellationToken = default) =>
            AsyncEnumerable.Empty<GalleryBlobDescriptor>();
    }

    private sealed class FailingOnPromoteSubmissionRepository : IPhotoSubmissionRepository
    {
        private readonly InMemoryPhotoSubmissionRepository inner = new();

        public Task<PhotoSubmission> CreateAsync(NewPhotoSubmission submission, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(submission, cancellationToken);

        public Task<IReadOnlyList<PhotoSubmissionListItem>> GetPendingAsync(
            int page, int pageSize, CancellationToken cancellationToken = default) =>
            inner.GetPendingAsync(page, pageSize, cancellationToken);

        public Task<PhotoSubmission?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            inner.GetByIdAsync(id, cancellationToken);

        public Task<SubmissionListPage<PhotoSubmission>> GetBySubmitterAsync(
            Guid submitterMemberId, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default) =>
            inner.GetBySubmitterAsync(submitterMemberId, page, pageSize, cancellationToken);

        public Task<PhotoSubmission?> UpdateStatusAsync(
            Guid id,
            string status,
            string? reviewerEmail,
            string? reviewNotes,
            string? rejectionReason,
            string? approvedCategory = null,
            CancellationToken cancellationToken = default) =>
            inner.UpdateStatusAsync(id, status, reviewerEmail, reviewNotes, rejectionReason, approvedCategory, cancellationToken);

        public Task<PhotoSubmission?> PromoteAsync(
            Guid id,
            int promotedPicId,
            string approvedCategory,
            string reviewerEmail,
            string? reviewNotes,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated DB write failure after blob upload.");

        public Task<SubmissionTypeCounts> GetDashboardCountsAsync(
            DateTimeOffset utcNow, CancellationToken cancellationToken = default) =>
            inner.GetDashboardCountsAsync(utcNow, cancellationToken);

        public Task<IReadOnlyList<SubmissionContributor>> GetTopContributorsThisMonthAsync(
            DateTimeOffset monthStart, int maxCount, CancellationToken cancellationToken = default) =>
            inner.GetTopContributorsThisMonthAsync(monthStart, maxCount, cancellationToken);
    }
}
