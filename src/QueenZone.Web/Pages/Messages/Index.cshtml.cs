using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Messages;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy)]
public sealed class IndexModel(PrivateMessageService privateMessageService) : PageModel
{
    public const string SuccessMessageKey = "MessagesInboxSuccess";

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

        Inbox = await privateMessageService.GetInboxAsync(
            memberId.Value,
            PageNumber,
            cancellationToken: cancellationToken);
        PageNumber = Inbox.Page;
        Pagination = ArchivePagination.BuildViewModel(
            "Inbox conversation pagination",
            Inbox.Page,
            Inbox.TotalPages,
            page => page <= 1 ? "/messages" : $"/messages?pageNumber={page}");
        StatusMessage = TempData[SuccessMessageKey] as string;
        ViewData["Title"] = "Messages";
        return Page();
    }

    public async Task<IActionResult> OnPostArchiveAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        var archived = await privateMessageService.ArchiveConversationAsync(
            conversationId,
            memberId.Value,
            cancellationToken);
        if (!archived)
        {
            return NotFound();
        }

        TempData[SuccessMessageKey] = "Conversation archived.";
        return RedirectToPage("./Index");
    }

    public async Task<IActionResult> OnPostRemoveAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        var removed = await privateMessageService.RemoveConversationAsync(
            conversationId,
            memberId.Value,
            cancellationToken);
        if (!removed)
        {
            return NotFound();
        }

        TempData[SuccessMessageKey] = "Conversation removed from your inbox.";
        return RedirectToPage("./Index");
    }
}
