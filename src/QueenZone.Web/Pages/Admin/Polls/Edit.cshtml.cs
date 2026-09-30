using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.Polls;

public sealed class EditModel(IHomePollRepository homePollRepository) : AdminPollPageModel
{
    public PollFormViewModel? Form { get; private set; }

    public HomePollAdminDetail? Poll { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken) =>
        LoadEditAsync(
            id,
            homePollRepository.GetByIdAsync,
            poll =>
            {
                Poll = poll;
                StatusMessage = TempData[MessageKey] as string;
                StatusMessageKind = TempData[MessageKindKey] as string;
                ViewData["Title"] = "Edit poll";
                Breadcrumbs = AdminBreadcrumbs.Page("Home polls", "/admin/polls", "Edit poll");
                Form = BuildForm(poll, ToDraft(poll), null);
            },
            cancellationToken);

    public static PollFormViewModel BuildForm(
        HomePollAdminDetail poll,
        AdminHomePollDraft draft,
        IReadOnlyList<string>? errors) =>
        new(
            "Edit poll",
            $"/admin/polls/{poll.Id}",
            draft,
            errors,
            OptionsLocked: poll.VoteCount > 0);

    public static AdminHomePollDraft ToDraft(HomePollAdminDetail poll) =>
        new(poll.Question, poll.Options.Select(option => option.OptionText).ToList());
}
