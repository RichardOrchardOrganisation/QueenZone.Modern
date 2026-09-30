using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Messages;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy)]
public sealed class IndexModel(PrivateMessageService privateMessageService) : InboxPageModel
{
    public const string SuccessMessageKey = "MessagesInboxSuccess";

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        LoadInboxAsync(privateMessageService, false, SuccessMessageKey, cancellationToken);

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
