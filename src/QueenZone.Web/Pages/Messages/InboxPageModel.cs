using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Messages;

public abstract class InboxPageModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public PrivateInboxPage Inbox { get; private set; } =
        new([], 0, 1, PrivateMessageLimits.InboxPageSize);

    public ArchivePaginationViewModel? Pagination { get; private set; }

    public string? StatusMessage { get; private set; }

    protected async Task<IActionResult> LoadInboxAsync(
        PrivateMessageService privateMessageService,
        bool archived,
        string successMessageKey,
        CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        Inbox = archived
            ? await privateMessageService.GetArchivedInboxAsync(memberId.Value, PageNumber, cancellationToken: cancellationToken)
            : await privateMessageService.GetInboxAsync(memberId.Value, PageNumber, cancellationToken: cancellationToken);
        PageNumber = Inbox.Page;

        var path = archived ? "/messages/archived" : "/messages";
        Pagination = ArchivePagination.BuildViewModel(
            archived ? "Archived conversation pagination" : "Inbox conversation pagination",
            Inbox.Page,
            Inbox.TotalPages,
            page => page <= 1 ? path : $"{path}?pageNumber={page}");
        StatusMessage = TempData[successMessageKey] as string;
        ViewData["Title"] = archived ? "Archived messages" : "Messages";
        return Page();
    }
}
