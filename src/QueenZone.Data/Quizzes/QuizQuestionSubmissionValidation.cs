namespace QueenZone.Data;

public static class QuizQuestionSubmissionValidation
{
    public const int MaxSourceNoteLength = 1000;

    public static IReadOnlyList<QuizQuestionSubmissionOptionDraft> NormalizeOptions(
        IEnumerable<QuizQuestionSubmissionOptionDraft> options) =>
        (options ?? [])
            .Where(option => !string.IsNullOrWhiteSpace(option.Text))
            .Select(option => option with { Text = option.Text.Trim() })
            .ToList();

    public static IReadOnlyList<string> ValidateSubmission(
        string questionText,
        IReadOnlyList<QuizQuestionSubmissionOptionDraft> options,
        string? sourceNote)
    {
        var errors = new List<string>();
        var text = questionText?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            errors.Add("Question text is required.");
        }
        else if (text.Length > QuizValidation.QuestionMaxLength)
        {
            errors.Add($"Question text must be {QuizValidation.QuestionMaxLength} characters or fewer.");
        }

        var normalized = NormalizeOptions(options);
        if (normalized.Count is < QuizValidation.MinOptions or > QuizValidation.MaxOptions)
        {
            errors.Add($"Provide between {QuizValidation.MinOptions} and {QuizValidation.MaxOptions} answer options.");
        }

        if (normalized.Any(option => option.Text.Length > QuizValidation.OptionMaxLength))
        {
            errors.Add($"Each option must be {QuizValidation.OptionMaxLength} characters or fewer.");
        }

        if (normalized.Count(option => option.IsCorrect) != 1)
        {
            errors.Add("Mark exactly one option as the correct answer.");
        }

        if (sourceNote is { Length: > MaxSourceNoteLength })
        {
            errors.Add($"Explanation or source note must be {MaxSourceNoteLength} characters or fewer.");
        }

        return errors;
    }
}
