using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Quizzes;

public sealed class EditPostModel(IQuizRepository quizRepository) : AdminQuizPageModel
{
    public QuizFormViewModel? Form { get; private set; }

    public QuizAdminDetail? Quiz { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnPostAsync(
        Guid id,
        [FromForm] AdminQuizForm form,
        CancellationToken cancellationToken)
    {
        var existing = await quizRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var draft = form.ToDraft();
        var errors = QuizValidation.ValidateDraft(draft).ToList();
        if (existing.AttemptCount > 0)
        {
            errors.Add("Questions and options cannot be changed after a quiz has results.");
        }

        if (errors.Count > 0)
        {
            ViewData["Title"] = "Edit quiz";
            Breadcrumbs = AdminBreadcrumbs.Page("Quizzes", "/admin/quizzes", "Edit quiz");
            Quiz = existing;
            Form = EditModel.BuildForm(existing, draft, errors);
            return Page();
        }

        try
        {
            await quizRepository.UpdateAsync(id, draft, cancellationToken);
        }
        catch (QuizException ex)
        {
            ViewData["Title"] = "Edit quiz";
            Breadcrumbs = AdminBreadcrumbs.Page("Quizzes", "/admin/quizzes", "Edit quiz");
            Quiz = existing;
            Form = EditModel.BuildForm(existing, draft, [ex.Message]);
            return Page();
        }

        TempData[MessageKey] = "Saved quiz.";
        TempData[MessageKindKey] = "success";
        return Redirect($"/admin/quizzes/{id}/edit");
    }
}
