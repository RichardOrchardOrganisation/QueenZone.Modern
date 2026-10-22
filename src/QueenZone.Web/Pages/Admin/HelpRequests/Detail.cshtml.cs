using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.HelpRequests;

public sealed class DetailModel(IHelpRequestRepository helpRequestRepository) : AdminHelpRequestsPageModel
{
    public HelpRequest? Item { get; private set; }

    public IReadOnlyList<HelpRequestReply> Replies { get; private set; } = [];

    public string ReplySubject { get; private set; } = string.Empty;

    public string ReplyBody { get; private set; } = string.Empty;

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        Item = await helpRequestRepository.GetByIdAsync(id, cancellationToken);
        if (Item is null)
        {
            return NotFound();
        }

        Replies = await helpRequestRepository.ListRepliesAsync(id, cancellationToken);
        ReplySubject = TempData["HelpRequestReplyDraftSubject"] as string
            ?? HelpRequestReplyService.DefaultSubject(Item.Subject);
        ReplyBody = TempData["HelpRequestReplyDraftBody"] as string ?? string.Empty;
        StatusMessage = TempData["HelpRequestMessage"] as string;
        StatusMessageKind = TempData["HelpRequestMessageKind"] as string;
        ViewData["Title"] = $"Help request — {Item.Subject}";
        Breadcrumbs = AdminBreadcrumbs.Page("Help requests", "/admin/help", Item.Subject);
        return Page();
    }
}
