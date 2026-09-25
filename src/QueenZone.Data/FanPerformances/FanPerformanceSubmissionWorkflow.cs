namespace QueenZone.Data;

/// <summary>
/// Allowed status transitions for member fan-performance submissions.
/// Photo-shaped, plus member withdraw to <see cref="FanPerformanceSubmissionStatus.Withdrawn"/>.
/// </summary>
public static class FanPerformanceSubmissionWorkflow
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [FanPerformanceSubmissionStatus.Pending] =
            [
                FanPerformanceSubmissionStatus.UnderReview,
                FanPerformanceSubmissionStatus.NeedsInfo,
                FanPerformanceSubmissionStatus.Approved,
                FanPerformanceSubmissionStatus.Rejected,
                FanPerformanceSubmissionStatus.Withdrawn,
            ],
            [FanPerformanceSubmissionStatus.UnderReview] =
            [
                FanPerformanceSubmissionStatus.NeedsInfo,
                FanPerformanceSubmissionStatus.Approved,
                FanPerformanceSubmissionStatus.Rejected,
                FanPerformanceSubmissionStatus.Withdrawn,
            ],
            [FanPerformanceSubmissionStatus.NeedsInfo] =
            [
                FanPerformanceSubmissionStatus.UnderReview,
                FanPerformanceSubmissionStatus.Approved,
                FanPerformanceSubmissionStatus.Rejected,
                FanPerformanceSubmissionStatus.Withdrawn,
            ],
            [FanPerformanceSubmissionStatus.Approved] = [],
            [FanPerformanceSubmissionStatus.Rejected] = [],
            [FanPerformanceSubmissionStatus.Withdrawn] = [],
        };

    private static readonly SubmissionWorkflowRules Rules = new(FanPerformanceSubmissionStatus.Statuses, AllowedTransitions);

    public static bool IsTerminal(string status) =>
        string.Equals(status, FanPerformanceSubmissionStatus.Approved, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, FanPerformanceSubmissionStatus.Rejected, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, FanPerformanceSubmissionStatus.Withdrawn, StringComparison.OrdinalIgnoreCase);

    public static bool CanMemberWithdraw(string status) =>
        CanTransition(status, FanPerformanceSubmissionStatus.Withdrawn);

    public static bool CanMemberReplyNeedsInfo(string status) =>
        FanPerformanceSubmissionStatus.IsKnown(status)
        && string.Equals(
            FanPerformanceSubmissionStatus.Normalize(status),
            FanPerformanceSubmissionStatus.NeedsInfo,
            StringComparison.Ordinal);

    public static bool CanAdminAct(string status) =>
        FanPerformanceSubmissionStatus.IsKnown(status) && !IsTerminal(status);

    public static bool CanTransition(string current, string next) =>
        Rules.CanTransition(current, next);

    public static bool TryValidateStatusChange(string current, string next, out string? error) =>
        Rules.TryValidateStatusChange(current, next, out error);
}
