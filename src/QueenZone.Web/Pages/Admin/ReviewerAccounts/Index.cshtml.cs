using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Admin.ReviewerAccounts;

public sealed class IndexModel(AdminReviewerAccountService reviewerAccounts) : AdminReviewerAccountsPageModel
{
    public IReadOnlyList<LocalPasswordAccountSummary> Accounts { get; private set; } = [];

    [BindProperty]
    public CreateReviewerAccountInput Input { get; set; } = new();

    public string? StatusMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        StatusMessage = TempData["ReviewerAccountMessage"] as string;
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        var result = await reviewerAccounts.CreateAsync(
            Input.Email,
            Input.DisplayName,
            Input.Password,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error!);
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["ReviewerAccountMessage"] = $"Created password sign-in for {result.Account!.Email}.";
        return RedirectToPage();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Accounts = await reviewerAccounts.ListAsync(cancellationToken);
        ViewData["Title"] = "Reviewer accounts";
    }
}

public sealed class CreateReviewerAccountInput
{
    [Required]
    [EmailAddress]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 2)]
    [Display(Name = "Display name")]
    public string DisplayName { get; set; } = "QueenZone reviewer account";

    [Required]
    [StringLength(256, MinimumLength = 12)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
