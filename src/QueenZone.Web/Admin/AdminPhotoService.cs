using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Storage;
using SixLabors.ImageSharp;

namespace QueenZone.Web;

/// <summary>
/// Outcome of hard-deleting a gallery photo row and best-effort CDN blob cleanup.
/// </summary>
public sealed record AdminPhotoDeleteResult(
    int PicId,
    int BlobsAttempted,
    int BlobsDeleted,
    int BlobsFailed,
    int BlobsUnresolved)
{
    public bool BlobCleanupSucceeded => BlobsFailed == 0 && BlobsUnresolved == 0;
}

/// <summary>
/// Orchestrates gallery admin uploads, replacements, hard deletes, and WebP thumbnail regeneration.
/// </summary>
public sealed class AdminPhotoService(
    IAdminPhotoRepository adminPhotoRepository,
    IGalleryPhotoBlobService galleryPhotoBlobService,
    ILogger<AdminPhotoService> logger)
{
    public async Task<int> CreateAsync(
        IFormFile file,
        int catId,
        string title,
        string? keywords,
        int year,
        DateTime dateTime,
        bool isVisible,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        var category = await adminPhotoRepository.GetCategoryByIdAsync(catId, cancellationToken)
            ?? throw new InvalidOperationException("Category was not found.");

        await using var uploadStream = file.OpenReadStream();
        var processed = await PhotoSubmissionImageProcessor.ProcessAsync(
            uploadStream,
            file.FileName,
            cancellationToken);

        try
        {
            var stem = Guid.NewGuid().ToString("N");
            var originalExtension = ResolveOriginalExtension(processed.MimeType, file.FileName);
            var originalFileName = stem + originalExtension;
            var thumbFileName = PhotoWebpDerivatives.ToThumbnailBlobName(originalFileName);
            var container = PhotoLegacyPath.BlobContainerName(category.Name);
            var legacyUrl = PhotoLegacyPath.BuildLegacyPath(category.Name, originalFileName);
            var legacyThumbUrl = PhotoLegacyPath.BuildLegacyPath(category.Name, thumbFileName);
            var thumbSize = PhotoWebpDerivatives.DefaultThumbSizePixels;

            processed.Original.Position = 0;
            await galleryPhotoBlobService.UploadAsync(
                container,
                originalFileName,
                processed.Original,
                processed.MimeType,
                cancellationToken);

            processed.Thumbnail.Position = 0;
            await galleryPhotoBlobService.UploadAsync(
                container,
                thumbFileName,
                processed.Thumbnail,
                PhotoWebpDerivatives.WebpContentType,
                cancellationToken);

            return await adminPhotoRepository.CreateAsync(
                new AdminPhotoCreateRequest(
                    catId,
                    title,
                    keywords,
                    year,
                    dateTime,
                    isVisible,
                    legacyUrl,
                    legacyThumbUrl,
                    thumbSize,
                    thumbSize,
                    processed.WidthPx,
                    processed.HeightPx),
                editorEmail,
                cancellationToken);
        }
        finally
        {
            await processed.Original.DisposeAsync();
            await processed.WebOptimized.DisposeAsync();
            await processed.Thumbnail.DisposeAsync();
        }
    }

    public async Task ReplaceAsync(
        int picId,
        IFormFile file,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        var existing = await adminPhotoRepository.GetByIdAsync(picId, cancellationToken)
            ?? throw new InvalidOperationException($"Photo {picId} was not found.");

        await using var uploadStream = file.OpenReadStream();
        var processed = await PhotoSubmissionImageProcessor.ProcessAsync(
            uploadStream,
            file.FileName,
            cancellationToken);

        try
        {
            var stem = Guid.NewGuid().ToString("N");
            var originalExtension = ResolveOriginalExtension(processed.MimeType, file.FileName);
            var originalFileName = stem + originalExtension;
            var thumbFileName = PhotoWebpDerivatives.ToThumbnailBlobName(originalFileName);
            var container = PhotoLegacyPath.BlobContainerName(existing.CategoryName);
            var legacyUrl = PhotoLegacyPath.BuildLegacyPath(existing.CategoryName, originalFileName);
            var legacyThumbUrl = PhotoLegacyPath.BuildLegacyPath(existing.CategoryName, thumbFileName);
            var thumbSize = PhotoWebpDerivatives.DefaultThumbSizePixels;

            processed.Original.Position = 0;
            await galleryPhotoBlobService.UploadAsync(
                container,
                originalFileName,
                processed.Original,
                processed.MimeType,
                cancellationToken);

            processed.Thumbnail.Position = 0;
            await galleryPhotoBlobService.UploadAsync(
                container,
                thumbFileName,
                processed.Thumbnail,
                PhotoWebpDerivatives.WebpContentType,
                cancellationToken);

            await adminPhotoRepository.UpdateAssetsAsync(
                picId,
                new AdminPhotoAssetUpdate(
                    legacyUrl,
                    legacyThumbUrl,
                    thumbSize,
                    thumbSize,
                    processed.WidthPx,
                    processed.HeightPx),
                editorEmail,
                cancellationToken);
        }
        finally
        {
            await processed.Original.DisposeAsync();
            await processed.WebOptimized.DisposeAsync();
            await processed.Thumbnail.DisposeAsync();
        }
    }

    public async Task<AdminPhotoDeleteResult> DeleteAsync(
        int picId,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        var existing = await adminPhotoRepository.GetByIdAsync(picId, cancellationToken)
            ?? throw new InvalidOperationException($"Photo {picId} was not found.");

        var (blobLocations, unresolvedCount) = ResolveBlobLocations(existing, picId);

        await adminPhotoRepository.DeleteAsync(picId, editorEmail, cancellationToken);

        var deletedCount = 0;
        var failedCount = 0;
        foreach (var (container, blobName) in blobLocations)
        {
            try
            {
                await galleryPhotoBlobService.DeleteAsync(container, blobName, cancellationToken);
                deletedCount++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failedCount++;
                logger.LogWarning(
                    ex,
                    "Gallery blob cleanup failed for photo {PicId} ({Container}/{BlobName})",
                    picId,
                    container,
                    blobName);
            }
        }

        return new AdminPhotoDeleteResult(
            picId,
            BlobsAttempted: blobLocations.Count,
            BlobsDeleted: deletedCount,
            BlobsFailed: failedCount,
            BlobsUnresolved: unresolvedCount);
    }

    /// <summary>Deletes all owned gallery blobs before removing the published row.</summary>
    public async Task DeleteForAccountDeletionAsync(int picId, CancellationToken cancellationToken = default)
    {
        var existing = await adminPhotoRepository.GetByIdAsync(picId, cancellationToken);
        if (existing is null)
        {
            return;
        }

        var (locations, unresolved) = ResolveBlobLocations(existing, picId);
        if (unresolved != 0)
        {
            throw new InvalidOperationException($"Photo {picId} has unresolved blob locations.");
        }

        foreach (var (container, blobName) in locations)
        {
            await galleryPhotoBlobService.DeleteAsync(container, blobName, cancellationToken);
        }

        await adminPhotoRepository.DeleteAsync(picId, "account-deletion@queenzone.org", cancellationToken);
    }

    public async Task RegenerateThumbnailAsync(
        int picId,
        string editorEmail,
        CancellationToken cancellationToken = default)
    {
        var existing = await adminPhotoRepository.GetByIdAsync(picId, cancellationToken)
            ?? throw new InvalidOperationException($"Photo {picId} was not found.");

        var blobUrl = PhotoImageUrl.ToBlobStorageUrl(existing.LegacyUrl);
        if (!PhotoImageUrl.TryParseBlobLocation(blobUrl, out var container, out var blobName))
        {
            throw new InvalidOperationException($"Could not parse blob location from Url '{existing.LegacyUrl}'.");
        }

        await using var source = await galleryPhotoBlobService.OpenReadAsync(container, blobName, cancellationToken);
        if (source is null)
        {
            throw new InvalidOperationException($"Source blob not found: {container}/{blobName}");
        }

        using var image = await Image.LoadAsync(source, cancellationToken);
        await using var thumb = await PhotoWebpDerivatives.CreateSquareThumbnailAsync(
            image,
            cancellationToken: cancellationToken);

        var thumbBlobName = PhotoWebpDerivatives.ToThumbnailBlobName(blobName);
        var legacyThumbPath = PhotoWebpDerivatives.ToLegacyThumbnailPath(existing.LegacyUrl, thumbBlobName);

        thumb.Stream.Position = 0;
        await galleryPhotoBlobService.UploadAsync(
            container,
            thumbBlobName,
            thumb.Stream,
            PhotoWebpDerivatives.WebpContentType,
            cancellationToken);

        await adminPhotoRepository.UpdateThumbnailAsync(
            picId,
            legacyThumbPath,
            thumb.WidthPx,
            thumb.HeightPx,
            editorEmail,
            cancellationToken);
    }

    private static string ResolveOriginalExtension(string mimeType, string fileName)
    {
        return mimeType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/tiff" => ".tif",
            _ => Path.GetExtension(fileName) is { Length: > 0 } ext ? ext.ToLowerInvariant() : ".jpg",
        };
    }

    private (List<(string Container, string BlobName)> Locations, int UnresolvedCount) ResolveBlobLocations(
        AdminPhotoItem photo,
        int picId)
    {
        var locations = new List<(string Container, string BlobName)>(capacity: 2);
        var unresolvedCount = 0;
        TryAdd(photo.LegacyUrl);
        TryAdd(photo.LegacyThumbUrl);
        return (locations, unresolvedCount);

        void TryAdd(string? legacyPath)
        {
            if (string.IsNullOrWhiteSpace(legacyPath))
            {
                return;
            }

            var blobUrl = PhotoImageUrl.ToBlobStorageUrl(legacyPath);
            if (PhotoImageUrl.TryParseBlobLocation(blobUrl, out var container, out var blobName))
            {
                locations.Add((container, blobName));
                return;
            }

            unresolvedCount++;
            logger.LogWarning(
                "Could not resolve gallery blob location for photo {PicId} path {LegacyPath}",
                picId,
                legacyPath);
        }
    }
}
