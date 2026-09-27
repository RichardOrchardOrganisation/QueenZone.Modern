using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Account;

public sealed class DeletionStatusModel(MemberDeletionReceiptService receipts) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Receipt { get; set; }

    public bool IsComplete { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var progress = await receipts.GetProgressAsync(Receipt, cancellationToken);
        if (progress is null)
        {
            return NotFound();
        }

        IsComplete = progress.IsComplete;
        ViewData["Title"] = IsComplete ? "Account deletion complete" : "Account deletion in progress";
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Page();
    }
}
