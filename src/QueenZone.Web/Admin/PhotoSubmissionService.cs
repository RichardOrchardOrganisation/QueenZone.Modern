using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

public sealed record PhotoSubmissionDetails(
    string Title,
    string? Description,
    string? SuggestedCategory,
    int? ApproximateYear,
    DateOnly? ApproximateDate);

public sealed class PhotoSubmissionService(
    IPhotoSubmissionRepository photoSubmissionRepository,
    IBlobUploadService blobUploadService,
    MemberUploadQuotaService uploadQuota)
{
    public sealed record SubmitResult(bool Succeeded, PhotoSubmission? Submission, string? Error);

    public async Task<SubmitResult> SubmitAsync(
        Guid memberAccountId,
        PhotoSubmissionDetails details,
        Stream photoStream,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        var title = details.Title;
        var description = details.Description;
        var suggestedCategory = details.SuggestedCategory;
        var approximateYear = details.ApproximateYear;
        var approximateDate = details.ApproximateDate;

        if (memberAccountId == Guid.Empty)
        {
            return new SubmitResult(false, null, "Sign in is required to submit a photo.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return new SubmitResult(false, null, "Title is required.");
        }

        if (title.Trim().Length > 200)
        {
            return new SubmitResult(false, null, "Title must be 200 characters or fewer.");
        }

        PhotoSubmissionImageProcessor.ProcessedPhotoSubmission processed;
        try
        {
            processed = await PhotoSubmissionImageProcessor.ProcessAsync(
                photoStream,
                originalFileName,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new SubmitResult(false, null, ex.Message);
        }

        var principalKey = MemberUploadQuotaService.PrincipalKeyFromMemberId(memberAccountId);
        if (!uploadQuota.TryConsume(principalKey, processed.OriginalSizeBytes, out var quotaError))
        {
            return new SubmitResult(false, null, quotaError ?? "Daily upload limit reached.");
        }

        await using (processed.Original)
        await using (processed.WebOptimized)
        await using (processed.Thumbnail)
        {
            var submissionId = Guid.NewGuid();
            var extension = Path.GetExtension(originalFileName);
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 16)
            {
                extension = MimeToExtension(processed.MimeType);
            }

            var folder = $"members/{memberAccountId:N}/{submissionId:N}";
            var originalBlobName = $"{folder}/original{extension.ToLowerInvariant()}";
            var webBlobName = $"{folder}/display.webp";
            var thumbBlobName = $"{folder}/thumb.webp";

            try
            {
                await blobUploadService.UploadAsync(
                    processed.Original,
                    Path.GetFileName(originalBlobName),
                    BlobUploadContainers.Photos,
                    new BlobUploadContext
                    {
                        MemberAccountId = memberAccountId,
                        PreferredBlobName = originalBlobName,
                    },
                    cancellationToken);

                processed.WebOptimized.Position = 0;
                await blobUploadService.UploadAsync(
                    processed.WebOptimized,
                    "display.webp",
                    BlobUploadContainers.Photos,
                    new BlobUploadContext
                    {
                        MemberAccountId = memberAccountId,
                        PreferredBlobName = webBlobName,
                    },
                    cancellationToken);

                processed.Thumbnail.Position = 0;
                await blobUploadService.UploadAsync(
                    processed.Thumbnail,
                    "thumb.webp",
                    BlobUploadContainers.Photos,
                    new BlobUploadContext
                    {
                        MemberAccountId = memberAccountId,
                        PreferredBlobName = thumbBlobName,
                    },
                    cancellationToken);
            }
            catch (BlobUploadException ex)
            {
                return new SubmitResult(false, null, ex.Message);
            }

            var created = await photoSubmissionRepository.CreateAsync(
                new NewPhotoSubmission(
                    memberAccountId,
                    title.Trim(),
                    description,
                    suggestedCategory,
                    approximateYear,
                    approximateDate,
                    originalBlobName,
                    webBlobName,
                    thumbBlobName,
                    Path.GetFileName(originalFileName),
                    processed.OriginalSizeBytes,
                    processed.MimeType,
                    processed.WidthPx,
                    processed.HeightPx,
                    submissionId),
                cancellationToken);

            return new SubmitResult(true, created, null);
        }
    }

    private static string MimeToExtension(string mimeType) =>
        mimeType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/tiff" => ".tiff",
            _ => ".bin",
        };
}
