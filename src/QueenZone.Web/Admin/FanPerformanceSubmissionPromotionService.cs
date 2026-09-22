using Microsoft.Extensions.Logging;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Copies an approved fan-performance submission into <c>songfiles</c> and
/// publishes a <c>Q_STAGE_T</c> row, recording <see cref="FanPerformanceSubmission.PromotedStageId"/>.
/// </summary>
public sealed class FanPerformanceSubmissionPromotionService(
    IFanPerformanceSubmissionRepository fanPerformanceSubmissionRepository,
    IAdminFanPerformanceRepository adminFanPerformanceRepository,
    IBlobUploadService blobUploadService,
    IServiceProvider serviceProvider,
    ILogger<FanPerformanceSubmissionPromotionService> logger)
{
    public async Task<int> PromoteAsync(
        FanPerformanceSubmission submission,
        string editorEmail,
        string? reviewNotes,
        FanPerformanceReviewEdits? edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(editorEmail);

        if (!FanPerformanceSubmissionRights.IsRecorded(
                submission.RightsDeclaredAt,
                submission.RightsDeclarationVersion))
        {
            throw new InvalidOperationException(
                "This submission has no rights declaration and cannot be published.");
        }

        if (edits is not null)
        {
            submission = await fanPerformanceSubmissionRepository.UpdateReviewMetadataAsync(
                submission.Id,
                edits,
                editorEmail,
                cancellationToken)
                ?? throw new InvalidOperationException("Submission was not found.");
        }

        if (submission.PromotedStageId is int existingId)
        {
            await EnsureApprovedAsync(submission, existingId, editorEmail, reviewNotes, cancellationToken);
            await TryDeletePendingBlobAsync(submission, cancellationToken);
            return existingId;
        }

        if (!FanPerformanceSubmissionWorkflow.CanTransition(
                submission.Status,
                FanPerformanceSubmissionStatus.Approved))
        {
            throw new InvalidOperationException(
                $"Cannot transition fan-performance submission status from {submission.Status} to {FanPerformanceSubmissionStatus.Approved}.");
        }

        var pending = await blobUploadService.OpenReadAsync(
            BlobUploadContainers.FanPerformances,
            submission.BlobPath,
            cancellationToken)
            ?? throw new InvalidOperationException("The submitted audio file is missing from storage.");

        var publishedName = BuildPublishedBlobName(submission);
        var wroteSongFile = false;
        await using (pending)
        {
            await blobUploadService.UploadAsync(
                pending.Stream,
                publishedName,
                SongFileUrl.ContainerName,
                new BlobUploadContext { PreferredBlobName = publishedName },
                cancellationToken);
            wroteSongFile = true;
        }

        try
        {
            var stageId = await ExecutePromotionAsync(
                submission,
                publishedName,
                editorEmail,
                reviewNotes,
                cancellationToken);
            await TryDeletePendingBlobAsync(submission, cancellationToken);
            return stageId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (wroteSongFile)
            {
                var latest = await fanPerformanceSubmissionRepository.GetByIdAsync(
                    submission.Id,
                    cancellationToken);
                if (latest?.PromotedStageId is int promotedStageId)
                {
                    await TryDeletePendingBlobAsync(latest, cancellationToken);
                    return promotedStageId;
                }

                if (!IsAlreadyApproved(latest))
                {
                    await CompensateUploadedBlobAsync(publishedName, submission.Id, cancellationToken);
                }
            }

            throw;
        }
    }

    internal static string BuildPublishedBlobName(FanPerformanceSubmission submission)
    {
        var extension = Path.GetExtension(submission.OriginalFileName);
        if (!IsAllowedAudioExtension(extension))
        {
            extension = IsFlacMime(submission.MimeType) ? ".flac" : ".mp3";
        }

        var name = $"{submission.Id:N}{extension.ToLowerInvariant()}";
        if (!SongFileUrl.IsSafeBlobName(name))
        {
            throw new InvalidOperationException("Published audio file name is not a safe bare filename.");
        }

        return name;
    }

    private async Task<int> ExecutePromotionAsync(
        FanPerformanceSubmission submission,
        string publishedName,
        string editorEmail,
        string? reviewNotes,
        CancellationToken cancellationToken)
    {
        return await QueenZoneDbTransactions.ExecuteAsync(
            serviceProvider,
            ct => PromoteCoreAsync(
                submission,
                publishedName,
                editorEmail,
                reviewNotes,
                ct),
            cancellationToken);
    }

    private async Task<int> PromoteCoreAsync(
        FanPerformanceSubmission submission,
        string publishedName,
        string editorEmail,
        string? reviewNotes,
        CancellationToken cancellationToken)
    {
        var stageId = await adminFanPerformanceRepository.CreateAsync(
            new AdminFanPerformanceCreateRequest(
                submission.Title,
                submission.PerformedBy,
                submission.Description ?? string.Empty,
                publishedName,
                submission.FileSizeBytes,
                DateTime.UtcNow,
                IsVisible: true),
            editorEmail,
            cancellationToken);

        _ = await fanPerformanceSubmissionRepository.PromoteAsync(
            submission.Id,
            stageId,
            editorEmail,
            reviewNotes,
            cancellationToken)
            ?? throw new InvalidOperationException("Promotion failed while updating the submission.");

        return stageId;
    }

    private async Task EnsureApprovedAsync(
        FanPerformanceSubmission submission,
        int promotedStageId,
        string editorEmail,
        string? reviewNotes,
        CancellationToken cancellationToken)
    {
        if (string.Equals(
                submission.Status,
                FanPerformanceSubmissionStatus.Approved,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = await fanPerformanceSubmissionRepository.PromoteAsync(
            submission.Id,
            promotedStageId,
            editorEmail,
            reviewNotes,
            cancellationToken)
            ?? throw new InvalidOperationException("Promotion failed while updating the submission.");
    }

    private async Task TryDeletePendingBlobAsync(
        FanPerformanceSubmission submission,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(submission.BlobPath))
        {
            return;
        }

        try
        {
            await blobUploadService.DeleteAsync(
                BlobUploadContainers.FanPerformances,
                submission.BlobPath,
                cancellationToken);
            await fanPerformanceSubmissionRepository.ClearPendingBlobPathAsync(submission.Id, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Pending fan-performance blob cleanup failed for submission {SubmissionId}",
                submission.Id);
        }
    }

    private async Task CompensateUploadedBlobAsync(
        string blobName,
        Guid submissionId,
        CancellationToken cancellationToken)
    {
        var latest = await fanPerformanceSubmissionRepository.GetByIdAsync(submissionId, cancellationToken);
        if (latest?.PromotedStageId is not null || IsAlreadyApproved(latest))
        {
            return;
        }

        try
        {
            await blobUploadService.DeleteAsync(SongFileUrl.ContainerName, blobName, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Orphan songfiles blob cleanup failed for submission {SubmissionId} ({Container}/{BlobName})",
                submissionId,
                SongFileUrl.ContainerName,
                blobName);
        }
    }

    private static bool IsAlreadyApproved(FanPerformanceSubmission? submission) =>
        submission is not null
        && string.Equals(
            submission.Status,
            FanPerformanceSubmissionStatus.Approved,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowedAudioExtension(string? extension) =>
        string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".flac", StringComparison.OrdinalIgnoreCase);

    private static bool IsFlacMime(string? mimeType) =>
        string.Equals(mimeType, "audio/flac", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimeType, "audio/x-flac", StringComparison.OrdinalIgnoreCase);
}
