namespace QueenZone.Data;

/// <summary>Allowed status transitions for member quiz question suggestions.</summary>
public static class QuizQuestionSubmissionWorkflow
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedTransitions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [QuizQuestionSubmissionStatus.Pending] =
            [
                QuizQuestionSubmissionStatus.Approved,
                QuizQuestionSubmissionStatus.Rejected,
            ],
            [QuizQuestionSubmissionStatus.Approved] = [],
            [QuizQuestionSubmissionStatus.Rejected] = [],
        };

    private static readonly SubmissionWorkflowRules Rules = new(QuizQuestionSubmissionStatus.Statuses, AllowedTransitions);

    public static bool IsTerminal(string status) =>
        string.Equals(status, QuizQuestionSubmissionStatus.Approved, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, QuizQuestionSubmissionStatus.Rejected, StringComparison.OrdinalIgnoreCase);

    public static bool CanTransition(string current, string next) =>
        Rules.CanTransition(current, next);

    public static bool TryValidateStatusChange(string current, string next, out string? error) =>
        Rules.TryValidateStatusChange(current, next, out error);
}
