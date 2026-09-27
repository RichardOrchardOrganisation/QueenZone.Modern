using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Trivia;

public sealed class EditModel(ITriviaRepository triviaRepository) : AdminTriviaPageModel
{
    public TriviaFormViewModel? Form { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken) =>
        LoadEditAsync(
            id,
            triviaRepository.GetByIdAsync,
            fact =>
            {
                StatusMessage = TempData[MessageKey] as string;
                StatusMessageKind = TempData[MessageKindKey] as string;
                ViewData["Title"] = "Edit trivia fact";
                Breadcrumbs = AdminBreadcrumbs.Page("Trivia", "/admin/trivia", "Edit trivia fact");
                Form = BuildForm(fact, ToDraft(fact), null);
            },
            cancellationToken);

    public static TriviaFormViewModel BuildForm(
        TriviaFactItem fact,
        AdminTriviaDraft draft,
        IReadOnlyList<string>? errors) =>
        new(
            "Edit trivia fact",
            $"/admin/trivia/{fact.Id}",
            draft,
            errors,
            fact);
}
