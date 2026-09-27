using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Biography;

public sealed class EditModel(IBiographyRepository biographyRepository) : AdminBiographyPageModel
{
    public ChapterFormViewModel? Form { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public Task<IActionResult> OnGetAsync(int id, CancellationToken cancellationToken) =>
        LoadEditAsync(
            id,
            biographyRepository.GetByIdAsync,
            chapter =>
            {
                StatusMessage = TempData[MessageKey] as string;
                StatusMessageKind = TempData[MessageKindKey] as string;
                ViewData["Title"] = "Edit biography chapter";
                Breadcrumbs = AdminBreadcrumbs.Page("Biography", "/admin/biography", "Edit chapter");
                Form = BuildForm(chapter, ToDraft(chapter), null);
            },
            cancellationToken);

    public static ChapterFormViewModel BuildForm(
        BiographyChapterItem chapter,
        AdminBiographyDraft draft,
        IReadOnlyList<string>? errors) =>
        new(
            "Edit biography chapter",
            $"/admin/biography/{chapter.Id}",
            draft,
            errors,
            chapter,
            chapter.Title);
}
