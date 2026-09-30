using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Messages;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy)]
public sealed class ArchivedModel(PrivateMessageService privateMessageService) : InboxPageModel
{
    public const string SuccessMessageKey = "MessagesArchivedSuccess";

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        LoadInboxAsync(privateMessageService, true, SuccessMessageKey, cancellationToken);

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
