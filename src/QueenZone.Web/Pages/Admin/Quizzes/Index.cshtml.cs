using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Quizzes;

public sealed class IndexModel(IQuizRepository quizRepository) : AdminQuizPageModel
{
    public IReadOnlyList<QuizAdminItem> Quizzes { get; private set; } = [];

    public QuizFormViewModel? CreateForm { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Quizzes = await quizRepository.GetAllAsync(cancellationToken);
        StatusMessage = TempData[MessageKey] as string;
        StatusMessageKind = TempData[MessageKindKey] as string;
        ViewData["Title"] = "Quizzes";
        Breadcrumbs = AdminBreadcrumbs.Section("Quizzes", "/admin/quizzes");
    }

    public async Task<IActionResult> OnPostAsync(
        [FromForm] AdminQuizForm form,
        CancellationToken cancellationToken)
    {
        var draft = form.ToDraft();
        var errors = QuizValidation.ValidateDraft(draft);
        if (errors.Count > 0)
        {
            ViewData["Title"] = "Add quiz";
            CreateForm = NewModel.BuildForm(draft, errors);
            return Page();
        }

        await quizRepository.CreateAsync(draft, Guid.Empty, cancellationToken);
        TempData[MessageKey] = "Added draft quiz.";
        TempData[MessageKindKey] = "success";
        return Redirect("/admin/quizzes");
    }

    public async Task<IActionResult> OnPostPublishAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await quizRepository.PublishAsync(id, cancellationToken);
            TempData[MessageKey] = "Published quiz. It is now playable publicly.";
            TempData[MessageKindKey] = "success";
        }
        catch (QuizException ex)
        {
            TempData[MessageKey] = ex.Message;
            TempData[MessageKindKey] = "error";
        }

        return Redirect("/admin/quizzes");
    }

    public async Task<IActionResult> OnPostUnpublishAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await quizRepository.UnpublishAsync(id, cancellationToken);
            TempData[MessageKey] = "Unpublished quiz. It is no longer playable publicly.";
            TempData[MessageKindKey] = "success";
        }
        catch (QuizException ex)
        {
            TempData[MessageKey] = ex.Message;
            TempData[MessageKindKey] = "error";
        }

        return Redirect("/admin/quizzes");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await quizRepository.DeleteAsync(id, cancellationToken);
            TempData[MessageKey] = "Deleted quiz.";
            TempData[MessageKindKey] = "success";
        }
        catch (QuizException ex)
        {
            TempData[MessageKey] = ex.Message;
            TempData[MessageKindKey] = "error";
        }

        return Redirect("/admin/quizzes");
    }
}
