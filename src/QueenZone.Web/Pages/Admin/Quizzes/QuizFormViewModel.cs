using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Quizzes;

public sealed record QuizFormViewModel(
    string Title,
    string Action,
    AdminQuizDraft Draft,
    IReadOnlyList<string>? Errors,
    bool QuestionsLocked = false)
{
    public static AdminQuizDraft EmptyDraft(int blankQuestions = 3) =>
        new(
            string.Empty,
            null,
            Enumerable.Range(0, blankQuestions)
                .Select(_ => new QuizQuestionDraft(
                    string.Empty,
                    QuizValidation.DefaultPoints,
                    [new QuizOptionDraft(string.Empty, false), new QuizOptionDraft(string.Empty, false)]))
                .ToList());

    public static AdminQuizDraft ToDraft(QuizAdminDetail quiz) =>
        new(
            quiz.Title,
            quiz.Description,
            quiz.Questions
                .Select(question => new QuizQuestionDraft(
                    question.Text,
                    question.Points,
                    question.Options
                        .Select(option => new QuizOptionDraft(option.Text, option.IsCorrect))
                        .ToList()))
                .ToList());
}
