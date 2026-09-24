using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

public sealed record GalleryOrphanSweepResult(
    int BlobsScanned,
    int OrphansFound,
    int OrphansDeleted,
    int DeleteFailures);

/// <summary>
/// Compares gallery blob storage against <c>PIC_FILES_T</c> per category and removes (or, in
/// dry-run mode, reports) blobs with no referencing row that are older than the configured
/// grace period. Complements the compensating delete in
/// <see cref="PhotoSubmissionPromotionService"/> (#590) by catching orphans left behind by the
/// residual crash window between a successful upload and that compensating delete (#651).
/// </summary>
public sealed class GalleryOrphanSweepService(
    IAdminPhotoRepository adminPhotoRepository,
    IGalleryPhotoBlobService galleryPhotoBlobService,
    TimeProvider timeProvider,
    IOptions<GalleryOrphanSweepOptions> options,
    ILogger<GalleryOrphanSweepService> logger)
{
    private const int CategorySweepMaxDegreeOfParallelism = 4;

    public async Task<GalleryOrphanSweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        var sweepOptions = options.Value;
        var cutoff = timeProvider.GetUtcNow() - TimeSpan.FromMinutes(sweepOptions.GracePeriodMinutes);
        var categories = await adminPhotoRepository.GetCategoriesAsync(cancellationToken);

        var scanned = 0;
        var found = 0;
        var deleted = 0;
        var failures = 0;

        await Parallel.ForEachAsync(
            categories,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = CategorySweepMaxDegreeOfParallelism,
                CancellationToken = cancellationToken,
            },
            async (category, categoryCancellationToken) =>
            {
                var container = PhotoLegacyPath.BlobContainerName(category.Name);
                HashSet<string>? referencedNames = null;

                // Stream the listing page by page instead of holding a whole container (#1677).
                await foreach (var blob in galleryPhotoBlobService.ListBlobsAsync(container, categoryCancellationToken))
                {
                    Interlocked.Increment(ref scanned);

                    // Loaded on the first blob so empty containers skip the database query.
                    referencedNames ??= new HashSet<string>(
                        await adminPhotoRepository.GetReferencedBlobNamesAsync(
                            category.CatId,
                            categoryCancellationToken),
                        StringComparer.OrdinalIgnoreCase);

                    if (referencedNames.Contains(blob.BlobName) || blob.LastModified > cutoff)
                    {
                        continue;
                    }

                    Interlocked.Increment(ref found);
                    if (sweepOptions.DryRun)
                    {
                        logger.LogInformation(
                            "Orphan gallery blob detected (dry run): {Container}/{BlobName}, last modified {LastModified}",
                            container,
                            blob.BlobName,
                            blob.LastModified);
                        continue;
                    }

                    try
                    {
                        await galleryPhotoBlobService.DeleteAsync(
                            container,
                            blob.BlobName,
                            categoryCancellationToken);
                        Interlocked.Increment(ref deleted);
                        logger.LogInformation(
                            "Deleted orphan gallery blob {Container}/{BlobName}", container, blob.BlobName);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        Interlocked.Increment(ref failures);
                        logger.LogWarning(
                            ex,
                            "Failed to delete orphan gallery blob {Container}/{BlobName}",
                            container,
                            blob.BlobName);
                    }
                }
            });

        return new GalleryOrphanSweepResult(scanned, found, deleted, failures);
    }
}
