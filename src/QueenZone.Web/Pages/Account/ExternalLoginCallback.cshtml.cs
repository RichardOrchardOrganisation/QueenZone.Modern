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

        var principal = externalResult.Principal;
        var provider = principal.Identities.First().AuthenticationType
            ?? throw new InvalidOperationException("External login is missing its provider scheme name.");
        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("External login did not return a subject id.");
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var displayName = principal.FindFirstValue(ClaimTypes.Name) ?? email ?? provider;
        var emailVerified = ExternalLoginEmail.IsVerified(provider, principal);
        var safeReturnUrl = LocalReturnUrl.Resolve(returnUrl);

        var resolution = await memberAccountService.FindOrCreateFromExternalLoginAsync(
            provider,
            providerKey,
            email,
            displayName,
            emailVerified,
            cancellationToken);

        var pending = await ExternalLoginLinkCookie.ReadAsync(HttpContext);
        await HttpContext.SignOutAsync(MemberAuthenticationSchemes.ExternalCookie);

        if (pending is not null)
        {
            return await ResumePendingLinkAsync(resolution, pending);
        }

        switch (resolution.Status)
        {
            case ExternalLoginStatus.SignedIn when resolution.Account is not null:
                await MemberCookieSignIn.SignInAsync(HttpContext, resolution.Account);
                return Redirect(safeReturnUrl);
            case ExternalLoginStatus.LinkConfirmationRequired:
                await ExternalLoginLinkCookie.SignInAsync(
                    HttpContext,
                    new PendingExternalLink(
                        MemberAuthenticationSchemes.NormalizeExternalProvider(provider) ?? provider,
                        providerKey,
                        email ?? string.Empty,
                        displayName,
                        safeReturnUrl,
                        MobileRequestId: null));
                return Redirect(ExternalLoginLinkCookie.PagePath);
            case ExternalLoginStatus.Suspended:
                return Redirect("/account/login?suspended=1");
            default:
                return Redirect("/account/login?externalEmail=unverified");
        }
    }

    private async Task<IActionResult> ResumePendingLinkAsync(
        ExternalLoginResolution resolution,
        PendingExternalLink pending)
    {
        var matched = resolution.Status == ExternalLoginStatus.SignedIn
            && resolution.Account is not null
            && ExternalLoginEmail.EmailsMatch(pending.Email, resolution.Account.Email);
        if (matched && resolution.Account is not null)
        {
            await MemberCookieSignIn.SignInAsync(HttpContext, resolution.Account);
            return Redirect(ExternalLoginLinkCookie.PagePath);
        }

        var error = resolution.Status switch
        {
            ExternalLoginStatus.Suspended => "suspended",
            ExternalLoginStatus.UnverifiedEmail => "unverified",
            _ => "provider",
        };
        return Redirect($"{ExternalLoginLinkCookie.PagePath}?linkError={error}");
    }
}
