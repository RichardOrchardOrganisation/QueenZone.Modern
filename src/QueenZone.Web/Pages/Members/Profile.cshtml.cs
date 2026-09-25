using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Pages.Members;

public sealed class ProfileModel(
    IMemberAccountRepository memberAccountRepository,
    IMemberPublicActivityRepository activityRepository,
    PrivateMessageService privateMessageService,
    MemberFollowService memberFollowService) : PageModel
{
    public const int ActivityPageSize = MemberPublicActivityPresentation.PageSize;

    public const string StatusMessageKey = "MemberProfileStatus";

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public MemberAccount? Member { get; private set; }

    public Guid? CurrentMemberId { get; private set; }

    public bool CanMessage { get; private set; }

    public bool IsSignedIn { get; private set; }

    public bool HasBlockedMember { get; private set; }

    public bool IsFollowingMember { get; private set; }

    public string? StatusMessage { get; private set; }

    public IReadOnlyList<MemberSocialLink> SocialLinks { get; private set; } = [];

    public IReadOnlyList<MemberActivityViewModel> Activity { get; private set; } = [];

    public ArchivePaginationViewModel? Pagination { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid memberId, CancellationToken cancellationToken)
    {
        if (PageNumber < 1)
        {
            return NotFound();
        }

        Member = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (Member is null || Member.DeletionRequestedAt is not null)
        {
            return NotFound();
        }

        CurrentMemberId = await HttpContext.GetSignedInMemberIdAsync();
        IsSignedIn = CurrentMemberId is not null;
        CanMessage = await privateMessageService.CanMessageAsync(
            CurrentMemberId,
            Member.Id,
            cancellationToken);
        if (CurrentMemberId is Guid current && current != Member.Id)
        {
            HasBlockedMember = await privateMessageService.HasBlockedAsync(
                current,
                Member.Id,
                cancellationToken);
            IsFollowingMember = await memberFollowService.IsFollowingAsync(
                current,
                Member.Id,
                cancellationToken);
        }

        SocialLinks = await memberAccountRepository.ListSocialLinksAsync(Member.Id, cancellationToken);
        StatusMessage = TempData[StatusMessageKey] as string;

        var activityPage = await activityRepository.GetPageAsync(
            memberId,
            Member.LinkedLegacyUserId,
            PageNumber,
            ActivityPageSize,
            cancellationToken);
        var totalPages = ArchivePagination.GetTotalPages(activityPage.TotalCount, ActivityPageSize);
        if (totalPages > 0 && PageNumber > totalPages)
        {
            return NotFound();
        }

        Activity = activityPage.Items.Select(MemberPublicActivityPresentation.ToViewModel).ToList();
        Pagination = ArchivePagination.BuildViewModel(
            "Member activity pagination",
            PageNumber,
            totalPages,
            page => page <= 1 ? $"/members/{memberId}" : $"/members/{memberId}?pageNumber={page}");
        ViewData["Title"] = Member.DisplayName;
        ViewData["CanonicalPath"] = PageNumber <= 1
            ? $"/members/{memberId}"
            : $"/members/{memberId}?pageNumber={PageNumber}";
        return Page();
    }

    public async Task<IActionResult> OnPostBlockAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var currentMemberId = await HttpContext.GetSignedInMemberIdAsync();
        if (currentMemberId is null)
        {
            return Challenge();
        }

        var result = await privateMessageService.BlockAsync(
            currentMemberId.Value,
            memberId,
            cancellationToken);
        if (!result.Succeeded)
        {
            if (string.Equals(result.ErrorMessage, "Member was not found.", StringComparison.Ordinal))
            {
                return NotFound();
            }

            TempData[StatusMessageKey] = result.ErrorMessage ?? "Unable to block member.";
            return RedirectToPage(new { memberId });
        }

        TempData[StatusMessageKey] = "Member blocked. They can no longer send you private messages.";
        return RedirectToPage(new { memberId });
    }

    public async Task<IActionResult> OnPostUnblockAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var currentMemberId = await HttpContext.GetSignedInMemberIdAsync();
        if (currentMemberId is null)
        {
            return Challenge();
        }

        var member = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        await privateMessageService.UnblockAsync(currentMemberId.Value, memberId, cancellationToken);
        TempData[StatusMessageKey] = "Member unblocked.";
        return RedirectToPage(new { memberId });
    }

    public async Task<IActionResult> OnPostFollowAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var currentMemberId = await HttpContext.GetSignedInMemberIdAsync();
        if (currentMemberId is null)
        {
            return Challenge();
        }

        var result = await memberFollowService.FollowAsync(
            currentMemberId.Value,
            memberId,
            cancellationToken);
        if (!result.Succeeded)
        {
            if (string.Equals(result.ErrorMessage, "Member was not found.", StringComparison.Ordinal))
            {
                return NotFound();
            }

            TempData[StatusMessageKey] = result.ErrorMessage ?? MemberFollowService.UnableToFollow;
            return RedirectToPage(new { memberId });
        }

        TempData[StatusMessageKey] = "You are now following this member.";
        return RedirectToPage(new { memberId });
    }

    public async Task<IActionResult> OnPostUnfollowAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var currentMemberId = await HttpContext.GetSignedInMemberIdAsync();
        if (currentMemberId is null)
        {
            return Challenge();
        }

        var member = await memberAccountRepository.FindByIdAsync(memberId, cancellationToken);
        if (member is null)
        {
            return NotFound();
        }

        await memberFollowService.UnfollowAsync(currentMemberId.Value, memberId, cancellationToken);
        TempData[StatusMessageKey] = "You unfollowed this member.";
        return RedirectToPage(new { memberId });
    }
}
