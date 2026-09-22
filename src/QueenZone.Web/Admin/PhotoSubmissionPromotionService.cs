using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Copies an approved member photo submission's assets into the public gallery
/// (legacy <c>PIC_FILES_T</c>/<c>PIC_CAT_T</c>) and records the promotion on the submission.
/// </summary>
public sealed class PhotoSubmissionPromotionService(
    IPhotoSubmissionRepository photoSubmissionRepository,
    IAdminPhotoRepository adminPhotoRepository,
    IBlobUploadService blobUploadService,
    IGalleryPhotoBlobService galleryPhotoBlobService,
    IServiceProvider serviceProvider,
    ILogger<PhotoSubmissionPromotionService> logger)
{
    public async Task<int> PromoteAsync(
        PhotoSubmission submission,
        string approvedCategory,
        string editorEmail,
        string? reviewNotes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvedCategory);

        var categories = await adminPhotoRepository.GetCategoriesAsync(cancellationToken);
        var category = categories.FirstOrDefault(c =>
            string.Equals(c.Name, approvedCategory.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Gallery category '{approvedCategory}' does not exist. Choose an existing category.");

        await using var original = await blobUploadService.OpenReadAsync(
            BlobUploadContainers.Photos, submission.WebOptimizedBlobPath, cancellationToken)
            ?? throw new InvalidOperationException("The submitted photo file is missing from storage.");
        await using var thumbnail = await blobUploadService.OpenReadAsync(
            BlobUploadContainers.Photos, submission.ThumbnailBlobPath, cancellationToken)
            ?? throw new InvalidOperationException("The submitted thumbnail is missing from storage.");

        var stem = Guid.NewGuid().ToString("N");
        var originalFileName = stem + PhotoWebpDerivatives.WebpExtension;
        var thumbFileName = PhotoWebpDerivatives.ToThumbnailBlobName(originalFileName);
        var container = PhotoLegacyPath.BlobContainerName(category.Name);
        var legacyUrl = PhotoLegacyPath.BuildLegacyPath(category.Name, originalFileName);
        var legacyThumbUrl = PhotoLegacyPath.BuildLegacyPath(category.Name, thumbFileName);
        var thumbSize = PhotoWebpDerivatives.DefaultThumbSizePixels;

        await galleryPhotoBlobService.UploadAsync(
            container, originalFileName, original.Stream, PhotoWebpDerivatives.WebpContentType, cancellationToken);
        await galleryPhotoBlobService.UploadAsync(
            container, thumbFileName, thumbnail.Stream, PhotoWebpDerivatives.WebpContentType, cancellationToken);

        try
        {
            return await QueenZoneDbTransactions.ExecuteAsync(
                serviceProvider,
                PromoteCoreAsync,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await CompensateUploadedBlobsAsync(container, originalFileName, thumbFileName, submission.Id, cancellationToken);
            throw;
        }

        async Task<int> PromoteCoreAsync(CancellationToken ct)
        {
            var picId = await adminPhotoRepository.CreateAsync(
                new AdminPhotoCreateRequest(
                    category.CatId,
                    submission.Title,
                    Keywords: null,
                    submission.ApproximateYear ?? DateTime.UtcNow.Year,
                    submission.ApproximateDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow,
                    IsVisible: true,
                    legacyUrl,
                    legacyThumbUrl,
                    thumbSize,
                    thumbSize,
                    submission.ImageWidthPx ?? 0,
                    submission.ImageHeightPx ?? 0),
                editorEmail,
                ct);

            _ = await photoSubmissionRepository.PromoteAsync(
                submission.Id, picId, category.Name, editorEmail, reviewNotes, ct)
                ?? throw new InvalidOperationException("Promotion failed while updating the submission.");

            return picId;
        }
    }

    private async Task CompensateUploadedBlobsAsync(
        string container,
        string originalFileName,
        string thumbFileName,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        foreach (var blobName in new[] { originalFileName, thumbFileName })
        {
            try
            {
                await galleryPhotoBlobService.DeleteAsync(container, blobName, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Orphan gallery blob cleanup failed for submission {SubmissionId} ({Container}/{BlobName})",
                    submissionId,
                    container,
                    blobName);
            }
        }
    }
}
