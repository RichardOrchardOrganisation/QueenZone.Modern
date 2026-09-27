using System.Security.Claims;
using QueenZone.Data;
using QueenZone.Storage;
using QueenZone.Web.Pages.Admin.News;

namespace QueenZone.Web;

/// <summary>
/// Stores article card images in <see cref="BlobUploadContainers.Articles"/>.
/// Replaced ugc-articles blobs are deleted only after the caller persists the new key.
/// Gallery / PIC references are never deleted. A new gallery pick is copied+cropped
/// into ugc-articles; the PIC original is read only.
/// </summary>
public sealed class NewsArticleImageService(
    IBlobUploadService blobUploadService,
    MemberUploadQuotaService uploadQuota,
    IAdminPhotoRepository adminPhotoRepository,
    IGalleryPhotoBlobService galleryPhotoBlobService)
{
    public sealed record ApplyResult(AdminNewsDraft Draft, string? Error);

    public async Task<ApplyResult> TryApplyAsync(
        IFormFile? file,
        NewsArticleImageCrop? crop,
        AdminNewsDraft draft,
        ClaimsPrincipal user,
        bool persist,
        CancellationToken cancellationToken = default) =>
        await TryApplyAsync(file, crop, draft, user, persist, persistedGalleryPicId: null, cancellationToken);

    public async Task<ApplyResult> TryApplyAsync(
        IFormFile? file,
        NewsArticleImageCrop? crop,
        AdminNewsDraft draft,
        ClaimsPrincipal user,
        bool persist,
        int? persistedGalleryPicId,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length <= 0)
        {
            return await TryApplyGalleryPickAsync(
                crop,
                draft,
                user,
                persist,
                persistedGalleryPicId,
                cancellationToken);
        }

        NewsArticleImageProcessor.ProcessedArticleImage processed;
        try
        {
            await using var source = file.OpenReadStream();
            processed = await NewsArticleImageProcessor.ProcessAsync(
                source,
                file.FileName,
                crop,
                NewsArticleImageProcessor.MaxUploadBytes,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new ApplyResult(draft, ex.Message);
        }

        return await PersistProcessedAsync(processed, draft, user, persist, cancellationToken);
    }

    private async Task<ApplyResult> TryApplyGalleryPickAsync(
        NewsArticleImageCrop? crop,
        AdminNewsDraft draft,
        ClaimsPrincipal user,
        bool persist,
        int? persistedGalleryPicId,
        CancellationToken cancellationToken)
    {
        if (draft.ImageGalleryPicId is not int picId)
        {
            return new ApplyResult(draft, null);
        }

        if (crop is null)
        {
            if (persistedGalleryPicId == picId)
            {
                return new ApplyResult(draft, null);
            }

            return new ApplyResult(draft, "Apply a 3:2 crop before saving this gallery photo.");
        }

        var original = await NewsArticleGalleryPicker.OpenOriginalAsync(
            adminPhotoRepository,
            galleryPhotoBlobService,
            picId,
            cancellationToken);
        if (original is null)
        {
            return new ApplyResult(draft, "That gallery photo could not be read.");
        }

        await using (original.Stream)
        {
            NewsArticleImageProcessor.ProcessedArticleImage processed;
            try
            {
                processed = await NewsArticleImageProcessor.ProcessRequiredCropAsync(
                    original.Stream,
                    original.FileName,
                    crop.Value,
                    NewsArticleImageProcessor.MaxUploadBytes,
                    cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return new ApplyResult(draft, ex.Message);
            }

            return await PersistProcessedAsync(processed, draft, user, persist, cancellationToken);
        }
    }

    private async Task<ApplyResult> PersistProcessedAsync(
        NewsArticleImageProcessor.ProcessedArticleImage processed,
        AdminNewsDraft draft,
        ClaimsPrincipal user,
        bool persist,
        CancellationToken cancellationToken)
    {
        await using (processed.FullImage)
        await using (processed.Thumbnail)
        {
            if (!persist)
            {
                return new ApplyResult(draft, null);
            }

            var principalKey = MemberUploadQuotaService.PrincipalKeyFromUser(user);
            var quotaBytes = Math.Max(processed.FullImage.Length, 1);
            if (!uploadQuota.TryConsume(principalKey, quotaBytes, out var quotaError))
            {
                return new ApplyResult(draft, quotaError ?? "Daily upload limit reached.");
            }

            var context = ImageUploadContextFactory.Create(user);
            string? uploadedFull = null;
            string? uploadedThumb = null;
            try
            {
                var fullName = BuildBlobName(context);

                processed.FullImage.Position = 0;
                var fullResult = await blobUploadService.UploadAsync(
                    processed.FullImage,
                    "article.webp",
                    BlobUploadContainers.Articles,
                    ImageUploadContextFactory.WithPreferredBlobName(context, fullName),
                    cancellationToken);
                uploadedFull = fullResult.BlobName;
                uploadedThumb = UgcProxyPaths.ToThumbBlobName(uploadedFull);

                processed.Thumbnail.Position = 0;
                await blobUploadService.UploadAsync(
                    processed.Thumbnail,
                    "article-thumb.webp",
                    BlobUploadContainers.Articles,
                    ImageUploadContextFactory.WithPreferredBlobName(context, uploadedThumb),
                    cancellationToken);

                // Persist the new key first (caller writes the draft), then delete the old blobs.
                return new ApplyResult(
                    draft with
                    {
                        ImageBlobKey = uploadedFull,
                        ImageGalleryPicId = null,
                    },
                    null);
            }
            catch (NotSupportedException ex)
            {
                await TryDeleteQuietlyAsync(uploadedFull, cancellationToken);
                await TryDeleteQuietlyAsync(uploadedThumb, cancellationToken);
                return new ApplyResult(draft, ex.Message);
            }
            catch (BlobUploadException ex)
            {
                await TryDeleteQuietlyAsync(uploadedFull, cancellationToken);
                await TryDeleteQuietlyAsync(uploadedThumb, cancellationToken);
                return new ApplyResult(draft, ex.Message);
            }
            catch (Exception)
            {
                await TryDeleteQuietlyAsync(uploadedFull, cancellationToken);
                await TryDeleteQuietlyAsync(uploadedThumb, cancellationToken);
                throw;
            }
        }
    }

    public Task TryDeletePreviousUgcArticlesAsync(
        string? previousImageBlobKey,
        string? replacementBlobName,
        CancellationToken cancellationToken = default) =>
        TryDeletePreviousUgcArticlesAsync(
            blobUploadService,
            previousImageBlobKey,
            replacementBlobName,
            cancellationToken);

    internal static async Task TryDeletePreviousUgcArticlesAsync(
        IBlobUploadService blobs,
        string? previousImageBlobKey,
        string? replacementBlobName,
        CancellationToken cancellationToken)
    {
        var previous = NewsArticleImage.ArticlesBlobName(previousImageBlobKey);
        if (string.IsNullOrWhiteSpace(previous))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(replacementBlobName)
            && string.Equals(previous, replacementBlobName, StringComparison.Ordinal))
        {
            return;
        }

        await TryDeleteQuietlyAsync(blobs, previous, cancellationToken);
        await TryDeleteQuietlyAsync(blobs, UgcProxyPaths.ToThumbBlobName(previous), cancellationToken);
    }

    private Task TryDeleteQuietlyAsync(string? blobName, CancellationToken cancellationToken) =>
        TryDeleteQuietlyAsync(blobUploadService, blobName, cancellationToken);

    private static async Task TryDeleteQuietlyAsync(
        IBlobUploadService blobs,
        string? blobName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobName))
        {
            return;
        }

        try
        {
            await blobs.DeleteAsync(BlobUploadContainers.Articles, blobName, cancellationToken);
        }
        catch
        {
            // Cleanup is best-effort, matching editor-image and avatar quiet-delete.
        }
    }

    private static string BuildBlobName(BlobUploadContext context)
    {
        var generated = BlobNameGenerator.Create("article.webp", context);
        if (!generated.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
        {
            generated += ".webp";
        }

        return generated;
    }

}
