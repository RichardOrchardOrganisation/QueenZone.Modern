using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Quizzes;

public sealed class AdminQuizQuestionForm
{
    [FromForm(Name = "text")]
    public string Text { get; init; } = string.Empty;

    [FromForm(Name = "points")]
    public int Points { get; init; } = QuizValidation.DefaultPoints;

    [FromForm(Name = "category")]
    public string? Category { get; init; }

    [FromForm(Name = "difficulty")]
    public string? Difficulty { get; init; }

    [FromForm(Name = "optionTexts")]
    public List<string> OptionTexts { get; init; } = [];

    [FromForm(Name = "correctOptionIndex")]
    public int CorrectOptionIndex { get; init; }
}

public sealed class AdminQuizForm
{
    [FromForm(Name = "title")]
    public string Title { get; init; } = string.Empty;

    [FromForm(Name = "description")]
    public string? Description { get; init; }

    [FromForm(Name = "questions")]
    public List<AdminQuizQuestionForm> Questions { get; init; } = [];

    public AdminQuizDraft ToDraft() =>
        new(
            (Title ?? string.Empty).Trim(),
            Description,
            Questions
                .Where(question => !string.IsNullOrWhiteSpace(question.Text)
                    || question.OptionTexts.Any(option => !string.IsNullOrWhiteSpace(option)))
                .Select(question => new QuizQuestionDraft(
                    (question.Text ?? string.Empty).Trim(),
                    question.Points <= 0 ? QuizValidation.DefaultPoints : question.Points,
                    question.OptionTexts
                        .Select((text, index) => new QuizOptionDraft(
                            text ?? string.Empty,
                            index == question.CorrectOptionIndex))
                        .ToList(),
                    string.IsNullOrWhiteSpace(question.Category) ? null : question.Category.Trim(),
                    string.IsNullOrWhiteSpace(question.Difficulty) ? null : question.Difficulty))
                .ToList());
}
