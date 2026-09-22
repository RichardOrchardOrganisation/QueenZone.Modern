using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Web.Infrastructure;

namespace QueenZone.Web.Pages.Account;

public sealed class ExternalLoginCallbackModel(MemberAccountService memberAccountService) : PageModel
{
    public async Task<IActionResult> OnGetAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        var externalResult = await HttpContext.AuthenticateAsync(MemberAuthenticationSchemes.ExternalCookie);
        if (!externalResult.Succeeded || externalResult.Principal is null)
        {
            return Redirect("/account/login");
        }

        var provider = externalResult.Principal.Identities.First().AuthenticationType
            ?? throw new InvalidOperationException("External login is missing its provider scheme name.");
        var providerKey = externalResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("External login did not return a subject id.");
        var email = externalResult.Principal.FindFirstValue(ClaimTypes.Email)
            ?? throw new InvalidOperationException("External login did not return an email address.");
        var displayName = externalResult.Principal.FindFirstValue(ClaimTypes.Name) ?? email;

        var account = await memberAccountService.FindOrCreateFromExternalLoginAsync(
            provider, providerKey, email, displayName, cancellationToken);

        await HttpContext.SignOutAsync(MemberAuthenticationSchemes.ExternalCookie);

        if (account.IsSuspended)
        {
            return Redirect("/account/login?suspended=1");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Email, account.Email),
            new Claim(ClaimTypes.Name, account.DisplayName),
            MemberSessionGate.CreateIssuedAtClaim(DateTimeOffset.UtcNow),
        };
        var identity = new ClaimsIdentity(claims, MemberAuthenticationSchemes.MembersCookie);
        await HttpContext.SignInAsync(MemberAuthenticationSchemes.MembersCookie, new ClaimsPrincipal(identity));

        return Redirect(LocalReturnUrl.Resolve(returnUrl));
    }
}
