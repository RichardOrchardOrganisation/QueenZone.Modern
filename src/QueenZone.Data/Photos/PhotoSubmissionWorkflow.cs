namespace QueenZone.Data;

/// <summary>
/// Allowed status transitions for member photo submissions.
/// </summary>
public static class PhotoSubmissionWorkflow
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PhotoSubmissionStatus.Pending] =
            [
                PhotoSubmissionStatus.UnderReview,
                PhotoSubmissionStatus.NeedsInfo,
                PhotoSubmissionStatus.Approved,
                PhotoSubmissionStatus.Rejected,
            ],
            [PhotoSubmissionStatus.UnderReview] =
            [
                PhotoSubmissionStatus.NeedsInfo,
                PhotoSubmissionStatus.Approved,
                PhotoSubmissionStatus.Rejected,
            ],
            [PhotoSubmissionStatus.NeedsInfo] =
            [
                PhotoSubmissionStatus.UnderReview,
                PhotoSubmissionStatus.Approved,
                PhotoSubmissionStatus.Rejected,
            ],
            [PhotoSubmissionStatus.Approved] = [],
            [PhotoSubmissionStatus.Rejected] = [],
        };

    private static readonly SubmissionWorkflowRules Rules = new(PhotoSubmissionStatus.Statuses, AllowedTransitions);

    public static bool IsTerminal(string status) =>
        string.Equals(status, PhotoSubmissionStatus.Approved, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, PhotoSubmissionStatus.Rejected, StringComparison.OrdinalIgnoreCase);

    public static bool CanTransition(string current, string next) =>
        Rules.CanTransition(current, next);

    public static bool TryValidateStatusChange(string current, string next, out string? error) =>
        Rules.TryValidateStatusChange(current, next, out error);
}
