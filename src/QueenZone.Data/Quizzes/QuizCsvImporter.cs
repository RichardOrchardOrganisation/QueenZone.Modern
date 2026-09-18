using Microsoft.VisualBasic.FileIO;

namespace QueenZone.Data;

/// <summary>
/// Bulk-creates draft (unpublished) quizzes from a CSV of question/option rows. Each row is one
/// answer option; rows are grouped into questions and quizzes by adjacency (rows for the same
/// quiz/question must be contiguous), matching how a spreadsheet of generated Q&amp;A is naturally
/// laid out. Imported quizzes are always brand new and always unpublished, so an admin reviews
/// and edits them in the normal quiz builder before publishing.
/// </summary>
public sealed class QuizCsvImporter(IQuizRepository quizRepository)
{
    private static readonly string[] ExpectedHeaders =
    [
        "QuizTitle",
        "QuizDescription",
        "QuestionText",
        "Category",
        "Difficulty",
        "Points",
        "OptionText",
        "IsCorrect",
    ];

    public async Task<QuizCsvImportResult> ImportAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        var drafts = ReadDrafts(csvPath);

        var errors = new List<string>();
        for (var index = 0; index < drafts.Count; index++)
        {
            var draftErrors = QuizValidation.ValidateDraft(drafts[index]);
            if (draftErrors.Count > 0)
            {
                errors.Add($"Quiz {index + 1} ('{drafts[index].Title}'): {string.Join(" ", draftErrors)}");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", errors));
        }

        var rowsRead = drafts.Sum(draft => draft.Questions.Sum(question => question.Options.Count));
        var questionsCreated = drafts.Sum(draft => draft.Questions.Count);
        var optionsCreated = rowsRead;

        foreach (var draft in drafts)
        {
            await quizRepository.CreateAsync(draft, Guid.Empty, cancellationToken);
        }

        return new QuizCsvImportResult(rowsRead, drafts.Count, questionsCreated, optionsCreated);
    }

    public static IReadOnlyList<AdminQuizDraft> ReadDrafts(string csvPath)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            throw new ArgumentException("CSV path is required.", nameof(csvPath));
        }

        using var parser = new TextFieldParser(csvPath);
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.TrimWhiteSpace = false;

        var headers = parser.ReadFields()
            ?? throw new InvalidOperationException("CSV file is empty.");
        if (!headers.SequenceEqual(ExpectedHeaders, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"CSV header must be: {string.Join(",", ExpectedHeaders)}");
        }

        var quizzes = new List<QuizGroup>();
        var closedQuizTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rowNumber = 1;
        while (!parser.EndOfData)
        {
            rowNumber++;
            var fields = parser.ReadFields();
            if (fields is null || fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (fields.Length != ExpectedHeaders.Length)
            {
                throw new InvalidOperationException($"Row {rowNumber} has {fields.Length} columns; expected {ExpectedHeaders.Length}.");
            }

            var row = ParseRow(fields, rowNumber);

            var quiz = quizzes.Count > 0 ? quizzes[^1] : null;
            if (quiz is null || !string.Equals(quiz.Title, row.QuizTitle, StringComparison.OrdinalIgnoreCase))
            {
                if (quiz is not null)
                {
                    closedQuizTitles.Add(quiz.Title);
                }

                if (closedQuizTitles.Contains(row.QuizTitle))
                {
                    throw new InvalidOperationException(
                        $"Row {rowNumber} continues quiz '{row.QuizTitle}', but its rows are not contiguous in the file.");
                }

                quiz = new QuizGroup(row.QuizTitle, row.QuizDescription);
                quizzes.Add(quiz);
            }

            var question = quiz.Questions.Count > 0 ? quiz.Questions[^1] : null;
            if (question is null || !string.Equals(question.Text, row.QuestionText, StringComparison.OrdinalIgnoreCase))
            {
                if (quiz.ClosedQuestionTexts.Contains(row.QuestionText))
                {
                    throw new InvalidOperationException(
                        $"Row {rowNumber} continues question '{row.QuestionText}', but its rows are not contiguous in the file.");
                }

                if (question is not null)
                {
                    quiz.ClosedQuestionTexts.Add(question.Text);
                }

                question = new QuestionGroup(row.QuestionText, row.Category, row.Difficulty, row.Points);
                quiz.Questions.Add(question);
            }

            question.Options.Add(new QuizOptionDraft(row.OptionText, row.IsCorrect));
        }

        return quizzes
            .Select(quiz => new AdminQuizDraft(
                quiz.Title,
                quiz.Description,
                quiz.Questions
                    .Select(question => new QuizQuestionDraft(
                        question.Text,
                        question.Points,
                        question.Options,
                        question.Category,
                        question.Difficulty))
                    .ToList()))
            .ToList();
    }

    private static QuizCsvImportRow ParseRow(string[] fields, int rowNumber)
    {
        var quizTitle = CsvImportRowParsing.Required(fields[0], rowNumber, "QuizTitle");
        var quizDescription = string.IsNullOrWhiteSpace(fields[1]) ? null : fields[1].Trim();
        var questionText = CsvImportRowParsing.Required(fields[2], rowNumber, "QuestionText");
        var category = string.IsNullOrWhiteSpace(fields[3]) ? null : fields[3].Trim();
        var difficulty = string.IsNullOrWhiteSpace(fields[4]) ? null : fields[4].Trim();
        var points = ParsePoints(fields[5], rowNumber);
        var optionText = CsvImportRowParsing.Required(fields[6], rowNumber, "OptionText");
        var isCorrect = ParseIsCorrect(fields[7], rowNumber);

        if (quizTitle.Length > QuizValidation.TitleMaxLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} QuizTitle must be {QuizValidation.TitleMaxLength} characters or fewer.");
        }

        if (quizDescription is not null && quizDescription.Length > QuizValidation.DescriptionMaxLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} QuizDescription must be {QuizValidation.DescriptionMaxLength} characters or fewer.");
        }

        if (questionText.Length > QuizValidation.QuestionMaxLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} QuestionText must be {QuizValidation.QuestionMaxLength} characters or fewer.");
        }

        if (category is not null && category.Length > QuizValidation.CategoryMaxLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} Category must be {QuizValidation.CategoryMaxLength} characters or fewer.");
        }

        if (difficulty is not null && !QuizValidation.AllowedDifficulties.Contains(difficulty, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"Row {rowNumber} Difficulty must be easy, medium, or hard.");
        }

        if (optionText.Length > QuizValidation.OptionMaxLength)
        {
            throw new InvalidOperationException($"Row {rowNumber} OptionText must be {QuizValidation.OptionMaxLength} characters or fewer.");
        }

        return new QuizCsvImportRow(quizTitle, quizDescription, questionText, category, difficulty, points, optionText, isCorrect);
    }

    private static int ParsePoints(string value, int rowNumber)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return QuizValidation.DefaultPoints;
        }

        return int.TryParse(value, out var points)
            ? points
            : throw new InvalidOperationException($"Row {rowNumber} Points must be a whole number.");
    }

    private static bool ParseIsCorrect(string value, int rowNumber) =>
        bool.TryParse(value, out var isCorrect)
            ? isCorrect
            : throw new InvalidOperationException($"Row {rowNumber} IsCorrect must be 'true' or 'false'.");

    private sealed class QuizGroup(string title, string? description)
    {
        public string Title { get; } = title;

        public string? Description { get; } = description;

        public List<QuestionGroup> Questions { get; } = [];

        public HashSet<string> ClosedQuestionTexts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class QuestionGroup(string text, string? category, string? difficulty, int points)
    {
        public string Text { get; } = text;

        public string? Category { get; } = category;

        public string? Difficulty { get; } = difficulty;

        public int Points { get; } = points;

        public List<QuizOptionDraft> Options { get; } = [];
    }
}

public sealed record QuizCsvImportRow(
    string QuizTitle,
    string? QuizDescription,
    string QuestionText,
    string? Category,
    string? Difficulty,
    int Points,
    string OptionText,
    bool IsCorrect);

public sealed record QuizCsvImportResult(int RowsRead, int QuizzesCreated, int QuestionsCreated, int OptionsCreated);
