using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Web.Sitemap;

namespace QueenZone.Web.Pages.Admin.Members;

public sealed class ActionModel(
    IMemberAccountRepository memberAccountRepository,
    IForumWriteRepository forumWriteRepository,
    ICoreSitemapService coreSitemapService,
    AdminMemberSuspendService adminMemberSuspendService) : AdminMembersPageModel
{
    [BindProperty]
    public string? Reason { get; set; }

    public async Task<IActionResult> OnPostSuspendAsync(Guid id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Reason))
        {
            return RedirectWithMessage(id, "A reason is required to suspend a member.", "error");
        }

        var result = await adminMemberSuspendService.SuspendAsync(
            id, Reason.Trim(), EditorEmail, DateTime.UtcNow, cancellationToken);

        return result.Status switch
        {
            AdminMemberSuspendStatus.NotFound => NotFound(),
            AdminMemberSuspendStatus.HideTimedOut => RedirectWithMessage(
                id, AdminMemberSuspendService.HideTimeoutMessage, "error"),
            AdminMemberSuspendStatus.RevokeFailed => RedirectWithMessage(
                id, AdminMemberSuspendService.RevokeFailedMessage, "error"),
            _ => RedirectWithMessage(id, AdminMemberSuspendService.SuccessMessage, "success"),
        };
    }

    public async Task<IActionResult> OnPostReinstateAsync(Guid id, CancellationToken cancellationToken)
    {
        var updated = await memberAccountRepository.ReinstateAsync(id, cancellationToken);
        if (updated is null)
        {
            return NotFound();
        }

        await forumWriteRepository.UnhideAuthorForumContentAsync(id, updated.DisplayName, cancellationToken);
        await coreSitemapService.InvalidateAsync(cancellationToken);

        return RedirectWithMessage(id, "Member reinstated and their forum posts restored.", "success");
    }

    public async Task<IActionResult> OnPostHideForumContentAsync(Guid id, CancellationToken cancellationToken)
    {
        var member = await memberAccountRepository.FindByIdAsync(id, cancellationToken);
        if (member is null) return NotFound();
        await forumWriteRepository.HideAuthorForumContentAsync(id, member.DisplayName, cancellationToken);
        await coreSitemapService.InvalidateAsync(cancellationToken);
        return RedirectWithMessage(id, $"All forum content by {member.DisplayName} is hidden.", "success");
    }

    public async Task<IActionResult> OnPostUnhideForumContentAsync(Guid id, CancellationToken cancellationToken)
    {
        var member = await memberAccountRepository.FindByIdAsync(id, cancellationToken);
        if (member is null) return NotFound();
        await forumWriteRepository.UnhideAuthorForumContentAsync(id, member.DisplayName, cancellationToken);
        await coreSitemapService.InvalidateAsync(cancellationToken);
        return RedirectWithMessage(id, $"All forum content by {member.DisplayName} is visible.", "success");
    }

    private IActionResult RedirectWithMessage(Guid id, string message, string kind)
    {
        TempData["MemberMessage"] = message;
        TempData["MemberMessageKind"] = kind;
        return Redirect($"/admin/members/{id}");
    }
}
