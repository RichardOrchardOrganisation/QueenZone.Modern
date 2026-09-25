using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Messages;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy)]
public sealed class ArchivedModel(PrivateMessageService privateMessageService) : PageModel
{
    public const string SuccessMessageKey = "MessagesArchivedSuccess";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PrivateInboxPage Inbox { get; private set; } =
        new([], 0, 1, PrivateMessageLimits.InboxPageSize);

    public ArchivePaginationViewModel? Pagination { get; private set; }

    public string? StatusMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        Inbox = await privateMessageService.GetArchivedInboxAsync(
            memberId.Value,
            PageNumber,
            cancellationToken: cancellationToken);
        PageNumber = Inbox.Page;
        Pagination = ArchivePagination.BuildViewModel(
            "Archived conversation pagination",
            Inbox.Page,
            Inbox.TotalPages,
            page => page <= 1 ? "/messages/archived" : $"/messages/archived?pageNumber={page}");
        StatusMessage = TempData[SuccessMessageKey] as string;
        ViewData["Title"] = "Archived messages";
        return Page();
    }

    public async Task<IActionResult> OnPostUnarchiveAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        var unarchived = await privateMessageService.UnarchiveConversationAsync(
            conversationId,
            memberId.Value,
            cancellationToken);
        if (!unarchived)
        {
            return NotFound();
        }

        TempData[SuccessMessageKey] = "Conversation moved back to your inbox.";
        return RedirectToPage("./Archived");
    }
}
