using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Application;

public sealed record FanPerformanceSubmissionPurgeResult(int Candidates, int Deleted, int Failures);

/// <summary>
/// Deletes pending <c>ugc-fan-performances</c> blobs for Rejected and Withdrawn
/// submissions after the grace period. Never touches <c>songfiles</c>.
/// </summary>
public sealed class FanPerformanceSubmissionPurgeService(
    IFanPerformanceSubmissionRepository fanPerformanceSubmissionRepository,
    IBlobUploadService blobUploadService,
    TimeProvider timeProvider,
    ILogger<FanPerformanceSubmissionPurgeService> logger)
{
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromDays(30);

    public TimeSpan GracePeriod { get; init; } = DefaultGracePeriod;

    public async Task<FanPerformanceSubmissionPurgeResult> PurgeAsync(
        CancellationToken cancellationToken = default)
    {
        var cutoff = timeProvider.GetUtcNow() - GracePeriod;
        var candidates = await fanPerformanceSubmissionRepository.GetEligibleForPendingBlobPurgeAsync(
            cutoff,
            cancellationToken);

        var deleted = 0;
        var failures = 0;
        foreach (var submission in candidates)
        {
            if (!IsPurgeEligible(submission, cutoff))
            {
                continue;
            }

            try
            {
                await blobUploadService.DeleteAsync(
                    BlobUploadContainers.FanPerformances,
                    submission.BlobPath,
                    cancellationToken);
                await fanPerformanceSubmissionRepository.ClearPendingBlobPathAsync(
                    submission.Id,
                    cancellationToken);
                deleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures++;
                logger.LogWarning(
                    ex,
                    "Pending fan-performance blob purge failed for submission {SubmissionId}",
                    submission.Id);
            }
        }

        return new FanPerformanceSubmissionPurgeResult(candidates.Count, deleted, failures);
    }

    internal static bool IsPurgeEligible(FanPerformanceSubmission submission, DateTimeOffset cutoffUtc)
    {
        if (string.IsNullOrWhiteSpace(submission.BlobPath))
        {
            return false;
        }

        if (submission.Status is not (FanPerformanceSubmissionStatus.Rejected
            or FanPerformanceSubmissionStatus.Withdrawn))
        {
            return false;
        }

        var markedAt = submission.ReviewedAt ?? submission.SubmittedAt;
        return markedAt <= cutoffUtc;
    }
}
