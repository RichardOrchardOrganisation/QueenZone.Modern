namespace QueenZone.Data;

/// <summary>
/// Allowed status transitions for member trivia fact suggestions.
/// </summary>
public static class TriviaFactSubmissionWorkflow
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [TriviaFactSubmissionStatus.Pending] =
            [
                TriviaFactSubmissionStatus.Approved,
                TriviaFactSubmissionStatus.Rejected,
            ],
            [TriviaFactSubmissionStatus.Approved] = [],
            [TriviaFactSubmissionStatus.Rejected] = [],
        };

    private static readonly SubmissionWorkflowRules Rules = new(TriviaFactSubmissionStatus.Statuses, AllowedTransitions);

    public static bool IsTerminal(string status) =>
        string.Equals(status, TriviaFactSubmissionStatus.Approved, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, TriviaFactSubmissionStatus.Rejected, StringComparison.OrdinalIgnoreCase);

    public static bool CanTransition(string current, string next) =>
        Rules.CanTransition(current, next);

    public static bool TryValidateStatusChange(string current, string next, out string? error) =>
        Rules.TryValidateStatusChange(current, next, out error);
}
