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

    public static bool IsTerminal(string status) =>
        string.Equals(status, QuizQuestionSubmissionStatus.Approved, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, QuizQuestionSubmissionStatus.Rejected, StringComparison.OrdinalIgnoreCase);

    public static bool CanTransition(string current, string next)
    {
        if (!QuizQuestionSubmissionStatus.IsKnown(current) || !QuizQuestionSubmissionStatus.IsKnown(next))
        {
            return false;
        }

        var normalizedCurrent = QuizQuestionSubmissionStatus.Normalize(current);
        var normalizedNext = QuizQuestionSubmissionStatus.Normalize(next);
        return AllowedTransitions.TryGetValue(normalizedCurrent, out var allowed)
            && allowed.Contains(normalizedNext, StringComparer.Ordinal);
    }

    public static bool TryValidateStatusChange(string current, string next, out string? error)
    {
        if (!QuizQuestionSubmissionStatus.IsKnown(current))
        {
            error = $"Unknown current status '{current}'.";
            return false;
        }

        if (!QuizQuestionSubmissionStatus.IsKnown(next))
        {
            error = $"Unknown target status '{next}'.";
            return false;
        }

        var normalizedCurrent = QuizQuestionSubmissionStatus.Normalize(current);
        var normalizedNext = QuizQuestionSubmissionStatus.Normalize(next);

        if (string.Equals(normalizedCurrent, normalizedNext, StringComparison.Ordinal))
        {
            error = $"This submission is already {normalizedNext}.";
            return false;
        }

        if (CanTransition(normalizedCurrent, normalizedNext))
        {
            error = null;
            return true;
        }

        error = $"Cannot transition quiz question submission status from {normalizedCurrent} to {normalizedNext}.";
        return false;
    }
}
