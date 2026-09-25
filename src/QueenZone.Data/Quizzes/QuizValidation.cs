namespace QueenZone.Data;

public static class QuizValidation
{
    public const int MinQuestions = 1;

    public const int MaxQuestions = 50;

    public const int MinOptions = 2;

    public const int MaxOptions = 4;

    public const int TitleMaxLength = 200;

    public const int DescriptionMaxLength = 1000;

    public const int QuestionMaxLength = 500;

    public const int OptionMaxLength = 200;

    public const int MinPoints = 1;

    public const int MaxPoints = 100;

    public const int DefaultPoints = 1;

    public const int CategoryMaxLength = 100;

    public const int DifficultyMaxLength = 20;

    public static readonly IReadOnlyList<string> AllowedDifficulties = ["easy", "medium", "hard"];

    public static IReadOnlyList<string> ValidateDraft(AdminQuizDraft draft)
    {
        var errors = new List<string>();
        var title = draft.Title?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            errors.Add("Title is required.");
        }
        else if (title.Length > TitleMaxLength)
        {
            errors.Add($"Title must be {TitleMaxLength} characters or fewer.");
        }

        var description = draft.Description?.Trim();
        if (description is { Length: > 0 } && description.Length > DescriptionMaxLength)
        {
            errors.Add($"Description must be {DescriptionMaxLength} characters or fewer.");
        }

        var questions = draft.Questions ?? [];
        if (questions.Count is < MinQuestions or > MaxQuestions)
        {
            errors.Add($"Quizzes require between {MinQuestions} and {MaxQuestions} questions.");
        }

        for (var index = 0; index < questions.Count; index++)
        {
            ValidateQuestion(questions[index], index, errors);
        }

        return errors;
    }

    private static void ValidateQuestion(QuizQuestionDraft question, int index, List<string> errors)
    {
        var label = $"Question {index + 1}";
        var text = question.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            errors.Add($"{label}: question text is required.");
        }
        else if (text.Length > QuestionMaxLength)
        {
            errors.Add($"{label}: question text must be {QuestionMaxLength} characters or fewer.");
        }

        if (question.Points is < MinPoints or > MaxPoints)
        {
            errors.Add($"{label}: points must be between {MinPoints} and {MaxPoints}.");
        }

        if (question.Category is { Length: > CategoryMaxLength })
        {
            errors.Add($"{label}: category must be {CategoryMaxLength} characters or fewer.");
        }

        if (question.Difficulty is not null
            && !AllowedDifficulties.Contains(question.Difficulty, StringComparer.Ordinal))
        {
            errors.Add($"{label}: difficulty must be easy, medium, or hard.");
        }

        var options = (question.Options ?? [])
            .Where(option => !string.IsNullOrWhiteSpace(option.Text))
            .ToList();
        if (options.Count is < MinOptions or > MaxOptions)
        {
            errors.Add($"{label}: requires between {MinOptions} and {MaxOptions} answer options.");
        }

        if (options.Any(option => option.Text.Trim().Length > OptionMaxLength))
        {
            errors.Add($"{label}: each option must be {OptionMaxLength} characters or fewer.");
        }

        if (options.Count(option => option.IsCorrect) != 1)
        {
            errors.Add($"{label}: exactly one option must be marked correct.");
        }
    }

    public static IReadOnlyList<QuizOptionDraft> NormalizeOptions(IEnumerable<QuizOptionDraft> options) =>
        (options ?? [])
            .Where(option => !string.IsNullOrWhiteSpace(option.Text))
            .Select(option => option with { Text = option.Text.Trim() })
            .ToList();
}
