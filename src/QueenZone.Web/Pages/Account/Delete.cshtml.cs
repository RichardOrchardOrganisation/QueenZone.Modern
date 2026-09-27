using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Account;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class DeleteModel(
    MemberAccountService memberAccountService,
    IMobileAuthGrantRepository mobileAuthGrantRepository,
    MemberDeletionReceiptService deletionReceipts) : PageModel
{
    [BindProperty]
    public string Confirmation { get; set; } = string.Empty;

    [BindProperty]
    public bool Immediate { get; set; }

    public string Email { get; private set; } = string.Empty;

    public DateTime? ScheduledDeletionAt { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var account = await LoadCurrentAccountAsync(cancellationToken);
        if (account is null)
        {
            return Redirect("/account/login");
        }

        PopulatePage(account);
        ViewData["Title"] = "Delete account";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var account = await LoadCurrentAccountAsync(cancellationToken);
        if (account is null)
        {
            return Redirect("/account/login");
        }

        PopulatePage(account);
        ViewData["Title"] = "Delete account";
        if (!string.Equals(Confirmation?.Trim(), "DELETE", StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(Confirmation), "Type DELETE to confirm account deletion.");
            return Page();
        }

        var result = Immediate
            ? await memberAccountService.DeleteImmediatelyAsync(account.Id, cancellationToken)
            : await memberAccountService.RequestDeletionAsync(account.Id, cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not request account deletion.");
            return Page();
        }

        await mobileAuthGrantRepository.RevokeAllRefreshTokensForMemberAsync(
            account.Id,
            DateTime.UtcNow,
            cancellationToken);
        await HttpContext.SignOutAsync(MemberAuthenticationSchemes.MembersCookie);
        return Redirect(Immediate
            ? "/account/deletion-status?receipt=" + Uri.EscapeDataString(deletionReceipts.Issue(account.Id))
            : "/account/deletion-requested");
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        var account = await LoadCurrentAccountAsync(cancellationToken);
        if (account is null)
        {
            return Redirect("/account/login");
        }

        var result = await memberAccountService.CancelDeletionAsync(account.Id, cancellationToken);
        if (!result.Succeeded)
        {
            PopulatePage(account);
            ViewData["Title"] = "Delete account";
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not cancel account deletion.");
            return Page();
        }

        TempData[SettingsModel.SuccessMessageKey] = "Account deletion cancelled.";
        return RedirectToPage("/Account/Settings");
    }

    private void PopulatePage(Data.Entities.MemberAccount account)
    {
        Email = account.Email;
        ScheduledDeletionAt = account.DeletionRequestedAt?.AddDays(MemberAccountDeletionPolicy.RetentionDays);
    }

    private async Task<Data.Entities.MemberAccount?> LoadCurrentAccountAsync(CancellationToken cancellationToken)
    {
        var authResult = await HttpContext.AuthenticateMemberAsync();
        var idValue = authResult.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return authResult.Succeeded && Guid.TryParse(idValue, out var memberId)
            ? await memberAccountService.FindByIdAsync(memberId, cancellationToken)
            : null;
    }
}
