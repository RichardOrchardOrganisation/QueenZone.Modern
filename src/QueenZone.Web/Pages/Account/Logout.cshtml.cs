using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Account;

public sealed class LogoutModel : PageModel
{
    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // SignOutAsync has no token; finish sign-out even if the request is aborted.
        _ = cancellationToken;
        await SignOutMemberAsync();
        return RedirectToSignedOutLogin();
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        await SignOutMemberAsync();
        return RedirectToSignedOutLogin();
    }

    private async Task SignOutMemberAsync()
    {
        await HttpContext.SignOutAsync(MemberAuthenticationSchemes.MembersCookie);
        await HttpContext.SignOutAsync(MemberAuthenticationSchemes.ExternalCookie);
        await HttpContext.SignOutAsync(MemberAuthenticationSchemes.ExternalLinkCookie);
    }

    private RedirectResult RedirectToSignedOutLogin() =>
        Redirect("/account/login?signedOut=1");
}
