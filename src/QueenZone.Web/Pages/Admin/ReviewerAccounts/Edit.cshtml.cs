using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.ReviewerAccounts;

public sealed class EditModel(AdminReviewerAccountService reviewerAccounts) : AdminReviewerAccountsPageModel
{
    public LocalPasswordAccountSummary Account { get; private set; } = null!;

    [BindProperty]
    public EditReviewerAccountInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await reviewerAccounts.FindAsync(id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        Account = account;
        Input.Email = account.Email;
        Input.DisplayName = account.DisplayName;
        SetTitle();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!string.Equals(Input.NewPassword, Input.ConfirmNewPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError("Input.ConfirmNewPassword", "Passwords do not match.");
        }

        if (!ModelState.IsValid)
        {
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }

        var result = await reviewerAccounts.UpdateAsync(
            id,
            Input.Email,
            Input.DisplayName,
            Input.NewPassword,
            cancellationToken);
        if (result.WasNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            return await ReloadOrNotFoundAsync(id, cancellationToken);
        }

        TempData["ReviewerAccountMessage"] = $"Updated password sign-in for {result.Account!.Email}.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await reviewerAccounts.RemovePasswordAsync(id, cancellationToken))
        {
            return NotFound();
        }

        TempData["ReviewerAccountMessage"] = "Removed password sign-in. The member record and its content were retained.";
        return RedirectToPage("Index");
    }

    private async Task<IActionResult> ReloadOrNotFoundAsync(Guid id, CancellationToken cancellationToken)
    {
        var account = await reviewerAccounts.FindAsync(id, cancellationToken);
        if (account is null)
        {
            return NotFound();
        }

        Account = account;
        SetTitle();
        return Page();
    }

    private void SetTitle() => ViewData["Title"] = $"Edit reviewer account — {Account.Email}";
}

public sealed class EditReviewerAccountInput
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string? ConfirmNewPassword { get; set; }
}
