namespace QueenZone.Data;

public static class TriviaValidation
{
    public const int MaxTextLength = 1000;

    public const int MaxCategoryLength = 100;

    public const int MaxDifficultyLength = 20;

    public const int MaxSourceLength = 500;

    public const int MaxSourceNoteLength = 1000;

    public static readonly IReadOnlyList<string> AllowedDifficulties = ["easy", "medium", "hard"];

    /// <summary>Trims <paramref name="value"/>; blank or whitespace-only input becomes <see langword="null"/>.</summary>
    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Trim-and-lowercase rule for trivia difficulty. Web forms pass no limit so
    /// <see cref="ValidateDraft"/> can still reject over-long input; the repositories pass
    /// <see cref="MaxDifficultyLength"/> to truncate what they store.
    /// </summary>
    public static string? NormalizeDifficulty(string? value, int maxLength = int.MaxValue)
    {
        var trimmed = NormalizeOptional(value);
        if (trimmed is null)
        {
            return null;
        }

        return (trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength]).ToLowerInvariant();
    }

    public static IReadOnlyList<string> ValidateDraft(AdminTriviaDraft draft)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(draft.Text))
        {
            errors.Add("Fact text is required.");
        }
        else if (draft.Text.Length > MaxTextLength)
        {
            errors.Add($"Fact text must be {MaxTextLength} characters or fewer.");
        }

        if (draft.Category is { Length: > MaxCategoryLength })
        {
            errors.Add($"Category must be {MaxCategoryLength} characters or fewer.");
        }

        if (draft.Difficulty is not null &&
            !AllowedDifficulties.Contains(draft.Difficulty, StringComparer.Ordinal))
        {
            errors.Add("Difficulty must be easy, medium, or hard.");
        }

        if (draft.Source is { Length: > MaxSourceLength })
        {
            errors.Add($"Source must be {MaxSourceLength} characters or fewer.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSuggestion(
        string text,
        string? category,
        string? difficulty,
        string? sourceNote)
    {
        var errors = ValidateDraft(new AdminTriviaDraft(text, false, category, difficulty)).ToList();

        if (sourceNote is { Length: > MaxSourceNoteLength })
        {
            errors.Add($"Source or context note must be {MaxSourceNoteLength} characters or fewer.");
        }

        return errors;
    }
}
