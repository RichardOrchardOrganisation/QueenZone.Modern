using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Messages;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy)]
public sealed class ComposeModel(
    PrivateMessageService privateMessageService,
    IMemberAccountRepository memberAccountRepository) : PageModel
{
    [BindProperty]
    public ComposeInput Input { get; set; } = new();

    public string? SelectedRecipientDisplayName { get; private set; }

    public IReadOnlyList<MemberRecipientMatch> RecipientMatches { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid? to, string? q, CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        ViewData["Title"] = "New message";

        if (to is Guid recipientId)
        {
            if (recipientId == memberId.Value)
            {
                ModelState.AddModelError(string.Empty, "You cannot message yourself.");
                return Page();
            }

            var recipient = await memberAccountRepository.FindByIdAsync(recipientId, cancellationToken);
            if (recipient is null || recipient.DeletionRequestedAt is not null)
            {
                ModelState.AddModelError(string.Empty, "Recipient was not found.");
                return Page();
            }

            if (!await privateMessageService.CanMessageAsync(memberId.Value, recipient.Id, cancellationToken))
            {
                ModelState.AddModelError(string.Empty, PrivateMessageService.UnableToSendMessage);
                return Page();
            }

            Input.RecipientMemberId = recipient.Id;
            SelectedRecipientDisplayName = recipient.DisplayName;
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            Input.RecipientQuery = q.Trim();
            RecipientMatches = await privateMessageService.SearchRecipientsAsync(
                memberId.Value,
                Input.RecipientQuery,
                cancellationToken);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.GetSignedInMemberIdAsync();
        if (memberId is null)
        {
            return Challenge();
        }

        ViewData["Title"] = "New message";

        if (Input.RecipientMemberId is null || Input.RecipientMemberId == Guid.Empty)
        {
            if (!string.IsNullOrWhiteSpace(Input.RecipientQuery))
            {
                RecipientMatches = await privateMessageService.SearchRecipientsAsync(
                    memberId.Value,
                    Input.RecipientQuery,
                    cancellationToken);
                if (RecipientMatches.Count == 1)
                {
                    Input.RecipientMemberId = RecipientMatches[0].MemberId;
                    SelectedRecipientDisplayName = RecipientMatches[0].DisplayName;
                }
                else if (RecipientMatches.Count == 0)
                {
                    ModelState.AddModelError(nameof(Input.RecipientQuery), "No members matched that name.");
                    return Page();
                }
                else
                {
                    ModelState.AddModelError(
                        nameof(Input.RecipientQuery),
                        "Multiple members matched. Select one from the list.");
                    return Page();
                }
            }
            else
            {
                ModelState.AddModelError(nameof(Input.RecipientQuery), "Choose a recipient.");
                return Page();
            }
        }
        else
        {
            var recipient = await memberAccountRepository.FindByIdAsync(
                Input.RecipientMemberId.Value,
                cancellationToken);
            SelectedRecipientDisplayName = recipient?.DisplayName;
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await privateMessageService.ComposeAsync(
            memberId.Value,
            Input.RecipientMemberId!.Value,
            Input.Body,
            cancellationToken);

        if (!result.Succeeded || result.ConversationId is null)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Unable to send message.");
            return Page();
        }

        return RedirectToPage("./Conversation", new { conversationId = result.ConversationId });
    }

    public sealed class ComposeInput
    {
        public Guid? RecipientMemberId { get; set; }

        [Display(Name = "To")]
        [StringLength(100)]
        public string? RecipientQuery { get; set; }

        [Display(Name = "Message")]
        [Required(ErrorMessage = "Message body is required.")]
        [StringLength(
            PrivateMessageLimits.MaxBodyLength,
            ErrorMessage = "Message body must be {1} characters or fewer.")]
        public string Body { get; set; } = string.Empty;
    }
}
