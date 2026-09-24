using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class GalleryOrphanSweepServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SweepAsync_DeletesUnreferencedBlobsOlderThanGracePeriod()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var container = PhotoLegacyPath.BlobContainerName("Queen");
        var galleryPhotoBlobService = new RecordingGalleryPhotoBlobService();
        galleryPhotoBlobService.Seed(container, "orphan.webp", Now - TimeSpan.FromHours(2));

        var service = CreateService(adminPhotoRepository, galleryPhotoBlobService, dryRun: false);

        var result = await service.SweepAsync();

        Assert.Equal(1, result.BlobsScanned);
        Assert.Equal(1, result.OrphansFound);
        Assert.Equal(1, result.OrphansDeleted);
        Assert.Equal(0, result.DeleteFailures);
        Assert.Contains(("orphan.webp"), galleryPhotoBlobService.Deleted.Select(d => d.BlobName));
    }

    [Fact]
    public async Task SweepAsync_IgnoresBlobsNewerThanGracePeriod()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var container = PhotoLegacyPath.BlobContainerName("Queen");
        var galleryPhotoBlobService = new RecordingGalleryPhotoBlobService();
        galleryPhotoBlobService.Seed(container, "fresh.webp", Now - TimeSpan.FromMinutes(5));

        var service = CreateService(adminPhotoRepository, galleryPhotoBlobService, dryRun: false);

        var result = await service.SweepAsync();

        Assert.Equal(0, result.OrphansFound);
        Assert.Empty(galleryPhotoBlobService.Deleted);
    }

    [Fact]
    public async Task SweepAsync_IgnoresReferencedBlobs()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var categories = await adminPhotoRepository.GetCategoriesAsync();
        var queen = categories.Single(c => c.Name == "Queen");
        var container = PhotoLegacyPath.BlobContainerName(queen.Name);

        var picId = await adminPhotoRepository.CreateAsync(
            new AdminPhotoCreateRequest(
                queen.CatId, "Referenced", null, 1986, DateTime.UtcNow, true,
                PhotoLegacyPath.BuildLegacyPath(queen.Name, "referenced.webp"),
                PhotoLegacyPath.BuildLegacyPath(queen.Name, "referenced_thumb.webp"),
                200, 200, 800, 600),
            "admin@test.local");
        Assert.True(picId > 0);

        var galleryPhotoBlobService = new RecordingGalleryPhotoBlobService();
        galleryPhotoBlobService.Seed(container, "referenced.webp", Now - TimeSpan.FromHours(2));
        galleryPhotoBlobService.Seed(container, "referenced_thumb.webp", Now - TimeSpan.FromHours(2));
        galleryPhotoBlobService.Seed(container, "orphan.webp", Now - TimeSpan.FromHours(2));

        var service = CreateService(adminPhotoRepository, galleryPhotoBlobService, dryRun: false);

        var result = await service.SweepAsync();

        Assert.Equal(1, result.OrphansFound);
        Assert.Equal(["orphan.webp"], galleryPhotoBlobService.Deleted.Select(d => d.BlobName));
    }

    [Fact]
    public async Task SweepAsync_DryRun_DoesNotDelete()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var container = PhotoLegacyPath.BlobContainerName("Queen");
        var galleryPhotoBlobService = new RecordingGalleryPhotoBlobService();
        galleryPhotoBlobService.Seed(container, "orphan.webp", Now - TimeSpan.FromHours(2));

        var service = CreateService(adminPhotoRepository, galleryPhotoBlobService, dryRun: true);

        var result = await service.SweepAsync();

        Assert.Equal(1, result.OrphansFound);
        Assert.Equal(0, result.OrphansDeleted);
        Assert.Empty(galleryPhotoBlobService.Deleted);
    }

    [Fact]
    public async Task SweepAsync_HonoursAlreadyCancelledToken()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var galleryPhotoBlobService = new RecordingGalleryPhotoBlobService();
        var service = CreateService(adminPhotoRepository, galleryPhotoBlobService, dryRun: false);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SweepAsync(cts.Token));
        Assert.Empty(galleryPhotoBlobService.Deleted);
    }

    [Fact]
    public async Task SweepAsync_CountsDeleteFailuresAndKeepsGoing()
    {
        var store = new SharedPhotoStore(SamplePhotoData.CreateSeedCategories());
        var adminPhotoRepository = new InMemoryAdminPhotoRepository(store);
        var container = PhotoLegacyPath.BlobContainerName("Queen");
        var galleryPhotoBlobService = new RecordingGalleryPhotoBlobService { FailDeletes = true };
        galleryPhotoBlobService.Seed(container, "orphan-one.webp", Now - TimeSpan.FromHours(2));
        galleryPhotoBlobService.Seed(container, "orphan-two.webp", Now - TimeSpan.FromHours(2));

        var service = CreateService(adminPhotoRepository, galleryPhotoBlobService, dryRun: false);

        var result = await service.SweepAsync();

        Assert.Equal(2, result.OrphansFound);
        Assert.Equal(0, result.OrphansDeleted);
        Assert.Equal(2, result.DeleteFailures);
    }

    private static GalleryOrphanSweepService CreateService(
        IAdminPhotoRepository adminPhotoRepository,
        IGalleryPhotoBlobService galleryPhotoBlobService,
        bool dryRun) =>
        new(
            adminPhotoRepository,
            galleryPhotoBlobService,
            new FixedClock(Now),
            Options.Create(new GalleryOrphanSweepOptions { DryRun = dryRun, GracePeriodMinutes = 60 }),
            NullLogger<GalleryOrphanSweepService>.Instance);

    private sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingGalleryPhotoBlobService : IGalleryPhotoBlobService
    {
        private readonly Dictionary<string, List<(string BlobName, DateTimeOffset LastModified)>> blobsByContainer =
            new(StringComparer.OrdinalIgnoreCase);

        public List<(string Container, string BlobName)> Deleted { get; } = [];

        public bool FailDeletes { get; init; }

        public bool IsConfigured => true;

        public void Seed(string containerName, string blobName, DateTimeOffset lastModified)
        {
            if (!blobsByContainer.TryGetValue(containerName, out var list))
            {
                list = [];
                blobsByContainer[containerName] = list;
            }

            list.Add((blobName, lastModified));
        }

        public Task UploadAsync(
            string containerName,
            string blobName,
            Stream content,
            string contentType,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by the sweep — it only lists and deletes.");

        public Task<Stream?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not used by the sweep — it only lists and deletes.");

        public Task DeleteAsync(string containerName, string blobName, CancellationToken cancellationToken = default)
        {
            if (FailDeletes)
            {
                throw new InvalidOperationException("Simulated blob delete failure.");
            }

            Deleted.Add((containerName, blobName));
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<GalleryBlobDescriptor> ListBlobsAsync(
            string containerName,
            CancellationToken cancellationToken = default)
        {
            if (!blobsByContainer.TryGetValue(containerName, out var list))
            {
                return AsyncEnumerable.Empty<GalleryBlobDescriptor>();
            }

            return list
                .Select(b => new GalleryBlobDescriptor(b.BlobName, b.LastModified))
                .ToList()
                .ToAsyncEnumerable();
        }
    }
}
