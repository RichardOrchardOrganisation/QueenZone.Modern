using QueenZone.Data.Entities;

namespace QueenZone.Data;

/// <summary>
/// Row construction and review transitions shared by the EF and in-memory photo submission
/// repositories, so both stores apply the same workflow rules.
/// </summary>
internal static class PhotoSubmissionRecords
{
    internal static PhotoSubmissionEntity NewEntity(NewPhotoSubmission submission) =>
        new()
        {
            Id = SubmissionInput.IdOrNew(submission.Id),
            SubmitterMemberId = submission.SubmitterMemberId,
            Title = submission.Title.Trim(),
            Description = SubmissionInput.NormalizeOptional(submission.Description, 1000),
            SuggestedCategory = SubmissionInput.NormalizeOptional(submission.SuggestedCategory, 100),
            ApproximateYear = submission.ApproximateYear,
            ApproximateDate = submission.ApproximateDate,
            BlobPath = submission.BlobPath.Trim(),
            WebOptimizedBlobPath = submission.WebOptimizedBlobPath.Trim(),
            ThumbnailBlobPath = submission.ThumbnailBlobPath.Trim(),
            OriginalFileName = submission.OriginalFileName.Trim(),
            FileSizeBytes = submission.FileSizeBytes,
            MimeType = submission.MimeType.Trim(),
            ImageWidthPx = submission.ImageWidthPx,
            ImageHeightPx = submission.ImageHeightPx,
            Status = PhotoSubmissionStatus.Pending,
            SubmittedAt = DateTimeOffset.UtcNow,
        };

    /// <summary>
    /// Validates and applies a review decision. Returns the normalised new status.
    /// </summary>
    internal static string ApplyStatusChange(
        PhotoSubmissionEntity entity,
        string status,
        string? reviewerEmail,
        string? reviewNotes,
        string? rejectionReason,
        string? approvedCategory)
    {
        if (!PhotoSubmissionWorkflow.TryValidateStatusChange(entity.Status, status, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var next = PhotoSubmissionStatus.Normalize(status);
        entity.Status = next;
        entity.ReviewedAt = DateTimeOffset.UtcNow;
        entity.ReviewerEmail = SubmissionInput.NormalizeOptional(reviewerEmail, 256);
        entity.ReviewNotes = SubmissionInput.NormalizeOptional(reviewNotes, 500);

        if (next == PhotoSubmissionStatus.Rejected)
        {
            entity.RejectionReason = SubmissionInput.NormalizeOptional(rejectionReason, 500)
                ?? throw new InvalidOperationException("A rejection reason is required.");
        }
        else if (!string.IsNullOrWhiteSpace(rejectionReason))
        {
            entity.RejectionReason = SubmissionInput.NormalizeOptional(rejectionReason, 500);
        }

        if (next == PhotoSubmissionStatus.Approved)
        {
            var category = SubmissionInput.NormalizeOptional(approvedCategory, 100)
                ?? SubmissionInput.NormalizeOptional(entity.SuggestedCategory, 100);
            entity.ApprovedCategory = category
                ?? throw new InvalidOperationException("An approved gallery category is required.");
        }
        else if (!string.IsNullOrWhiteSpace(approvedCategory))
        {
            entity.ApprovedCategory = SubmissionInput.NormalizeOptional(approvedCategory, 100);
        }

        return next;
    }
}
