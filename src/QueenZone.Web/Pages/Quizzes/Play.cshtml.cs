using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Quizzes;

public sealed class PlayModel(IQuizRepository quizRepository) : PageModel
{
    public QuizPlayView? Quiz { get; private set; }

    public QuizSubmissionResult? Result { get; private set; }

    public bool ViewerIsSignedIn { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Quiz = await quizRepository.GetPublishedForPlayAsync(id, cancellationToken);
        if (Quiz is null)
        {
            return NotFound();
        }

        SetViewData(Quiz);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        Quiz = await quizRepository.GetPublishedForPlayAsync(id, cancellationToken);
        if (Quiz is null)
        {
            return NotFound();
        }

        SetViewData(Quiz);

        var answers = Quiz.Questions
            .Select(question => new QuizAnswerSubmission(question.Id, ReadSelectedOptionId(question.Id)))
            .ToList();

        var memberAuth = await HttpContext.AuthenticateMemberAsync();
        var memberId = ForumMember.GetMemberId(memberAuth.Principal);
        ViewerIsSignedIn = memberId is not null;

        Result = await quizRepository.SubmitAsync(id, memberId, answers, cancellationToken);
        return Page();
    }

    private Guid? ReadSelectedOptionId(Guid questionId)
    {
        if (Request.Form.TryGetValue($"answer_{questionId:N}", out var raw)
            && Guid.TryParse(raw, out var optionId))
        {
            return optionId;
        }

        return null;
    }

    private void SetViewData(QuizPlayView quiz)
    {
        ViewData["Title"] = $"{quiz.Title} | QueenZone Quiz";
        ViewData["CanonicalPath"] = $"/quizzes/{quiz.Id}";
        Breadcrumbs =
        [
            BreadcrumbItem.Home,
            new BreadcrumbItem("Quiz", "/quizzes"),
            new BreadcrumbItem(quiz.Title, $"/quizzes/{quiz.Id}"),
        ];
    }
}
