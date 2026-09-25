using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class QuizValidationTests
{
    [Fact]
    public void ValidateDraft_rejects_blank_title_and_no_questions()
    {
        var errors = QuizValidation.ValidateDraft(new AdminQuizDraft("  ", null, []));

        Assert.Contains(errors, error => error.Contains("Title is required", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("between 1 and 50 questions", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDraft_rejects_overlong_title_and_question_and_option()
    {
        var draft = new AdminQuizDraft(
            new string('T', QuizValidation.TitleMaxLength + 1),
            null,
            [
                new QuizQuestionDraft(
                    new string('Q', QuizValidation.QuestionMaxLength + 1),
                    1,
                    [
                        new QuizOptionDraft(new string('O', QuizValidation.OptionMaxLength + 1), true),
                        new QuizOptionDraft("ok", false),
                    ]),
            ]);

        var errors = QuizValidation.ValidateDraft(draft);

        Assert.Contains(errors, error => error.Contains("Title must be 200", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("question text must be 500", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("each option must be 200", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDraft_requires_exactly_one_correct_option()
    {
        var noneCorrect = new AdminQuizDraft(
            "Title",
            null,
            [new QuizQuestionDraft("Q1", 1, [new QuizOptionDraft("A", false), new QuizOptionDraft("B", false)])]);
        var bothCorrect = new AdminQuizDraft(
            "Title",
            null,
            [new QuizQuestionDraft("Q1", 1, [new QuizOptionDraft("A", true), new QuizOptionDraft("B", true)])]);

        Assert.Contains(
            QuizValidation.ValidateDraft(noneCorrect),
            error => error.Contains("exactly one option must be marked correct", StringComparison.Ordinal));
        Assert.Contains(
            QuizValidation.ValidateDraft(bothCorrect),
            error => error.Contains("exactly one option must be marked correct", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDraft_rejects_too_few_or_too_many_options()
    {
        var oneOption = new AdminQuizDraft(
            "Title",
            null,
            [new QuizQuestionDraft("Q1", 1, [new QuizOptionDraft("A", true)])]);
        var fiveOptions = new AdminQuizDraft(
            "Title",
            null,
            [
                new QuizQuestionDraft(
                    "Q1",
                    1,
                    [
                        new QuizOptionDraft("A", true),
                        new QuizOptionDraft("B", false),
                        new QuizOptionDraft("C", false),
                        new QuizOptionDraft("D", false),
                        new QuizOptionDraft("E", false),
                    ]),
            ]);

        Assert.Contains(
            QuizValidation.ValidateDraft(oneOption),
            error => error.Contains("between 2 and 4 answer options", StringComparison.Ordinal));
        Assert.Contains(
            QuizValidation.ValidateDraft(fiveOptions),
            error => error.Contains("between 2 and 4 answer options", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDraft_accepts_a_well_formed_quiz()
    {
        var draft = new AdminQuizDraft(
            "Queen trivia",
            "A short quiz",
            [
                new QuizQuestionDraft(
                    "Who was the lead singer?",
                    2,
                    [new QuizOptionDraft("Freddie Mercury", true), new QuizOptionDraft("Brian May", false)]),
            ]);

        Assert.Empty(QuizValidation.ValidateDraft(draft));
    }

    [Fact]
    public void ValidateDraft_rejects_overlong_category_and_unsupported_difficulty()
    {
        var draft = new AdminQuizDraft(
            "Title",
            null,
            [
                new QuizQuestionDraft(
                    "Q1",
                    1,
                    [new QuizOptionDraft("A", true), new QuizOptionDraft("B", false)],
                    new string('C', QuizValidation.CategoryMaxLength + 1),
                    "impossible"),
            ]);

        var errors = QuizValidation.ValidateDraft(draft);

        Assert.Contains(errors, error => error.Contains("category must be 100", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("difficulty must be easy, medium, or hard", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateDraft_accepts_a_question_with_category_and_difficulty()
    {
        var draft = new AdminQuizDraft(
            "Queen trivia",
            null,
            [
                new QuizQuestionDraft(
                    "Who was the lead singer?",
                    1,
                    [new QuizOptionDraft("Freddie Mercury", true), new QuizOptionDraft("Brian May", false)],
                    "Band Members",
                    "easy"),
            ]);

        Assert.Empty(QuizValidation.ValidateDraft(draft));
    }

    [Fact]
    public void NormalizeOptions_trims_and_drops_blank_options()
    {
        var normalized = QuizValidation.NormalizeOptions(
        [
            new QuizOptionDraft("  A  ", true),
            new QuizOptionDraft("   ", false),
            new QuizOptionDraft("B", false),
        ]);

        Assert.Equal(2, normalized.Count);
        Assert.Equal("A", normalized[0].Text);
        Assert.True(normalized[0].IsCorrect);
        Assert.Equal("B", normalized[1].Text);
    }
}
