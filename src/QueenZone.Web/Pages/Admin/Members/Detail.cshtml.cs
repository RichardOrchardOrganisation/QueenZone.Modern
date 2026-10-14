using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Pages.Admin.Members;

public sealed class DetailModel(
    IMemberAccountRepository memberAccountRepository,
    IForumWriteRepository forumWriteRepository) : AdminMembersPageModel
{
    public MemberAccount? Member { get; private set; }

    public string? StatusMessage { get; private set; }

    public string? StatusMessageKind { get; private set; }

    public ForumAuthorContentSummary? ForumContent { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        HttpContext.Items[MemberDetailFailureMiddleware.StageKey] = "member-lookup";
        Member = await memberAccountRepository.FindByIdAsync(id, cancellationToken);
        if (Member is null)
        {
            return NotFound();
        }

        HttpContext.Items[MemberDetailFailureMiddleware.StageKey] = "forum-summary";
        ForumContent = await forumWriteRepository.GetAuthorForumContentSummaryAsync(
            Member.Id, Member.DisplayName, cancellationToken);

        HttpContext.Items[MemberDetailFailureMiddleware.StageKey] = "render";
        StatusMessage = TempData["MemberMessage"] as string;
        StatusMessageKind = TempData["MemberMessageKind"] as string;
        ViewData["Title"] = $"Member — {Member.DisplayName}";
        Breadcrumbs = AdminBreadcrumbs.Page("Members", "/admin/members", Member.DisplayName);
        return Page();
    }
}
