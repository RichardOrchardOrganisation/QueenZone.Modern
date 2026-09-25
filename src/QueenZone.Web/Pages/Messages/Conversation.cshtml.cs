using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Messages;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy)]
public sealed class ConversationModel(PrivateMessageService privateMessageService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid ConversationId { get; set; }

    /// <summary>
    /// Optional 1-based page. When omitted, the latest page (newest messages) is shown.
    /// </summary>
    [BindProperty(SupportsGet = true)]
    public int? PageNumber { get; set; }

    public PrivateConversationDetail? Detail { get; private set; }

    public ArchivePaginationViewModel? Pagination { get; private set; }

    public bool HasBlockedOtherParticipant { get; private set; }

    public bool CanSendReply { get; private set; }

    public string? StatusMessage { get; private set; }

    [BindProperty]
    public ReplyInput Input { get; set; } = new();

    [BindProperty]
    public ReportInputModel ReportInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        Detail = await privateMessageService.GetConversationAsync(
            ConversationId,
            memberId.Value,
            markRead: true,
            page: PageNumber,
            cancellationToken: cancellationToken);
        if (Detail is null)
        {
            return NotFound();
        }

        await PopulateConversationChromeAsync(memberId.Value, cancellationToken);
        StatusMessage = TempData[IndexModel.SuccessMessageKey] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        if (!ModelState.IsValid)
        {
            Detail = await privateMessageService.GetConversationAsync(
                ConversationId,
                memberId.Value,
                markRead: false,
                page: PageNumber,
                cancellationToken: cancellationToken);
            if (Detail is null)
            {
                return NotFound();
            }

            await PopulateConversationChromeAsync(memberId.Value, cancellationToken);
            return Page();
        }

        var result = await privateMessageService.ReplyAsync(
            ConversationId,
            memberId.Value,
            Input.Body,
            cancellationToken);

        if (!result.Succeeded)
        {
            if (string.Equals(
                    result.ErrorMessage,
                    "You are not a participant in this conversation.",
                    StringComparison.Ordinal))
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to send reply.");
            Detail = await privateMessageService.GetConversationAsync(
                ConversationId,
                memberId.Value,
                markRead: false,
                page: PageNumber,
                cancellationToken: cancellationToken);
            if (Detail is null)
            {
                return NotFound();
            }

            await PopulateConversationChromeAsync(memberId.Value, cancellationToken);
            return Page();
        }

        // Redirect to the latest page so the new reply is visible.
        return RedirectToPage("./Conversation", new { conversationId = ConversationId });
    }

    public async Task<IActionResult> OnPostArchiveAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        var archived = await privateMessageService.ArchiveConversationAsync(
            ConversationId,
            memberId.Value,
            cancellationToken);
        if (!archived)
        {
            return NotFound();
        }

        TempData[IndexModel.SuccessMessageKey] = "Conversation archived.";
        return RedirectToPage("./Index");
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        var removed = await privateMessageService.RemoveConversationAsync(
            ConversationId,
            memberId.Value,
            cancellationToken);
        if (!removed)
        {
            return NotFound();
        }

        TempData[IndexModel.SuccessMessageKey] = "Conversation removed from your inbox.";
        return RedirectToPage("./Index");
    }

    public async Task<IActionResult> OnPostBlockAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        var detail = await privateMessageService.GetConversationAsync(
            ConversationId,
            memberId.Value,
            markRead: false,
            page: PageNumber,
            cancellationToken: cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        var result = await privateMessageService.BlockAsync(
            memberId.Value,
            detail.OtherParticipantId,
            cancellationToken);
        if (!result.Succeeded)
        {
            TempData[IndexModel.SuccessMessageKey] = result.ErrorMessage ?? "Unable to block member.";
            return RedirectToPage(new { conversationId = ConversationId, pageNumber = PageNumber });
        }

        TempData[IndexModel.SuccessMessageKey] =
            "Member blocked. They can no longer send you private messages.";
        return RedirectToPage(new { conversationId = ConversationId, pageNumber = PageNumber });
    }

    public async Task<IActionResult> OnPostUnblockAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        var detail = await privateMessageService.GetConversationAsync(
            ConversationId,
            memberId.Value,
            markRead: false,
            page: PageNumber,
            cancellationToken: cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        await privateMessageService.UnblockAsync(
            memberId.Value,
            detail.OtherParticipantId,
            cancellationToken);
        TempData[IndexModel.SuccessMessageKey] = "Member unblocked.";
        return RedirectToPage(new { conversationId = ConversationId, pageNumber = PageNumber });
    }

    public async Task<IActionResult> OnPostReportAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        if (ReportInput.MessageId == Guid.Empty)
        {
            return NotFound();
        }

        var result = await privateMessageService.ReportMessageAsync(
            memberId.Value,
            ConversationId,
            ReportInput.MessageId,
            ReportInput.Reason,
            cancellationToken);
        if (!result.Succeeded)
        {
            if (string.Equals(result.ErrorMessage, PrivateMessageReportText.NotAParticipant, StringComparison.Ordinal)
                || string.Equals(result.ErrorMessage, PrivateMessageReportText.MessageNotFound, StringComparison.Ordinal))
            {
                return NotFound();
            }

            TempData[IndexModel.SuccessMessageKey] = result.ErrorMessage ?? "Unable to report this message.";
            return RedirectToPage(new { conversationId = ConversationId, pageNumber = PageNumber });
        }

        TempData[IndexModel.SuccessMessageKey] = result.AlreadyReported
            ? "You have already reported this message."
            : "Message reported. Moderators will review it. The other person is not notified.";
        return RedirectToPage(new { conversationId = ConversationId, pageNumber = PageNumber });
    }

    private async Task PopulateConversationChromeAsync(Guid memberId, CancellationToken cancellationToken)
    {
        if (Detail is null)
        {
            return;
        }

        PageNumber = Detail.Page;
        Pagination = BuildPagination(Detail);
        HasBlockedOtherParticipant = await privateMessageService.HasBlockedAsync(
            memberId,
            Detail.OtherParticipantId,
            cancellationToken);
        CanSendReply = await privateMessageService.CanSendReplyAsync(
            memberId,
            Detail,
            cancellationToken);
        ViewData["Title"] = $"Message {Detail.OtherParticipantDisplayName}";
    }

    private ArchivePaginationViewModel? BuildPagination(PrivateConversationDetail detail) =>
        ArchivePagination.BuildViewModel(
            "Conversation message pagination",
            detail.Page,
            detail.TotalPages,
            page => page >= detail.TotalPages
                ? $"/messages/{ConversationId}"
                : $"/messages/{ConversationId}?pageNumber={page}");

    public sealed class ReplyInput
    {
        [Display(Name = "Reply")]
        [Required(ErrorMessage = "Message body is required.")]
        [StringLength(
            PrivateMessageLimits.MaxBodyLength,
            ErrorMessage = "Message body must be {1} characters or fewer.")]
        public string Body { get; set; } = string.Empty;
    }

    public sealed class ReportInputModel
    {
        public Guid MessageId { get; set; }

        [Display(Name = "Optional reason")]
        [StringLength(
            PrivateMessageLimits.MaxReportReasonLength,
            ErrorMessage = "Report reason must be {1} characters or fewer.")]
        public string? Reason { get; set; }
    }
}
