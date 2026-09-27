using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Pages.Account;

public sealed class LinkExternalLoginModel(
    IOptions<MemberAuthenticationOptions> memberAuthenticationOptions,
    MemberAccountService memberAccountService,
    MobileAuthService mobileAuth,
    AppleAccountTokenService appleTokens) : AccountPageModel(memberAuthenticationOptions)
{
    public string Provider { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public bool HasPassword { get; private set; }

    public bool IsSignedInAsTarget { get; private set; }

    public string? SignedInEmail { get; private set; }

    public IReadOnlyList<string> ConfirmationProviders { get; private set; } = [];

    public string? Error { get; private set; }

    public string? LinkError { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? linkError, CancellationToken cancellationToken)
    {
        var pending = await ExternalLoginLinkCookie.ReadAsync(HttpContext);
        if (pending is null)
        {
            return Redirect("/account/login?externalLink=expired");
        }

        LinkError = DescribeLinkError(linkError);
        await PopulateAsync(pending, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken cancellationToken)
    {
        var pending = await ExternalLoginLinkCookie.ReadAsync(HttpContext);
        if (pending is null)
        {
            return Redirect("/account/login?externalLink=expired");
        }

        var signedIn = await ReadSignedInMemberAsync(cancellationToken);
        if (signedIn is null || !ExternalLoginEmail.EmailsMatch(signedIn.Email, pending.Email))
        {
            Error = ExternalLoginMessages.EmailMismatch;
            await PopulateAsync(pending, cancellationToken);
            return Page();
        }

        return await LinkAndFinishAsync(pending, signedIn.Id, cancellationToken);
    }

    public async Task<IActionResult> OnPostPasswordAsync(string? password, CancellationToken cancellationToken)
    {
        var pending = await ExternalLoginLinkCookie.ReadAsync(HttpContext);
        if (pending is null)
        {
            return Redirect("/account/login?externalLink=expired");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            Error = "Enter your password.";
            await PopulateAsync(pending, cancellationToken);
            return Page();
        }

        var signIn = await memberAccountService.SignInAsync(pending.Email, password, cancellationToken);
        if (!signIn.Succeeded || signIn.Account is null)
        {
            Error = signIn.Error ?? "Incorrect email or password.";
            await PopulateAsync(pending, cancellationToken);
            return Page();
        }

        return await LinkAndFinishAsync(pending, signIn.Account.Id, cancellationToken);
    }

    public async Task<IActionResult> OnPostCancelAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var pending = await ExternalLoginLinkCookie.ReadAsync(HttpContext);
        await ExternalLoginLinkCookie.SignOutAsync(HttpContext);
        if (pending?.MobileRequestId is null)
        {
            return Redirect("/account/login");
        }

        var cancelled = mobileAuth.CancelPendingAuthorization(pending.MobileRequestId);
        if (cancelled?.RedirectUri is null)
        {
            return Redirect("/account/login");
        }

        var location = MobileAuthEndpoints.BuildAppRedirect(
            cancelled.RedirectUri,
            cancelled.State,
            error: cancelled.Error,
            description: cancelled.ErrorDescription);
        HttpContext.Response.Redirect(location);
        return new EmptyResult();
    }

    internal static IReadOnlyList<string> SelectConfirmationProviders(
        IReadOnlyList<string> linkedProviders,
        Func<string, bool> isEnabled)
    {
        ArgumentNullException.ThrowIfNull(linkedProviders);
        ArgumentNullException.ThrowIfNull(isEnabled);

        return linkedProviders
            .Where(provider => isEnabled(provider))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IActionResult> LinkAndFinishAsync(
        PendingExternalLink pending,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var linked = await memberAccountService.LinkExternalLoginAsync(
            memberId,
            pending.Provider,
            pending.ProviderKey,
            pending.Email,
            cancellationToken);
        if (!linked.Succeeded || linked.Account is null)
        {
            Error = linked.Error ?? "Could not link that sign-in provider.";
            await PopulateAsync(pending, cancellationToken);
            return Page();
        }

        if (pending.Provider == MemberAuthenticationSchemes.Apple
            && pending.ProtectedAppleRefreshToken is not null)
        {
            await appleTokens.SaveProtectedAsync(
                linked.Account.Id,
                pending.ProviderKey,
                pending.ProtectedAppleRefreshToken,
                cancellationToken);
        }

        await ExternalLoginLinkCookie.SignOutAsync(HttpContext);
        await MemberCookieSignIn.SignInAsync(HttpContext, linked.Account);

        if (string.IsNullOrWhiteSpace(pending.MobileRequestId))
        {
            return Redirect(pending.ReturnUrl);
        }

        var completed = await mobileAuth.CompleteConfirmedLoginAsync(
            pending.MobileRequestId,
            linked.Account,
            cancellationToken);
        if (!completed.Success || completed.RedirectUri is null)
        {
            Error = completed.ErrorDescription ?? "Could not finish mobile sign-in.";
            return Page();
        }

        var location = MobileAuthEndpoints.BuildAppRedirect(
            completed.RedirectUri,
            completed.State,
            code: completed.Code);
        HttpContext.Response.Redirect(location);
        return new EmptyResult();
    }

    private async Task PopulateAsync(PendingExternalLink pending, CancellationToken cancellationToken)
    {
        Provider = pending.Provider;
        Email = pending.Email;

        var account = await memberAccountService.FindByEmailAsync(pending.Email, cancellationToken);
        HasPassword = account?.PasswordHash is not null;
        if (account is not null)
        {
            var linked = await memberAccountService.ListExternalProvidersAsync(account.Id, cancellationToken);
            ConfirmationProviders = SelectConfirmationProviders(linked, IsProviderEnabled);
        }

        var signedIn = await ReadSignedInMemberAsync(cancellationToken);
        SignedInEmail = signedIn?.Email;
        IsSignedInAsTarget = signedIn is not null && ExternalLoginEmail.EmailsMatch(signedIn.Email, pending.Email);
    }

    private bool IsProviderEnabled(string provider) => provider switch
    {
        MemberAuthenticationSchemes.Google => GoogleEnabled,
        MemberAuthenticationSchemes.Microsoft => MicrosoftEnabled,
        MemberAuthenticationSchemes.Discord => DiscordEnabled,
        MemberAuthenticationSchemes.GitHub => GitHubEnabled,
        MemberAuthenticationSchemes.Apple => AppleEnabled,
        _ => false,
    };

    private async Task<MemberAccount?> ReadSignedInMemberAsync(CancellationToken cancellationToken)
    {
        var result = await HttpContext.AuthenticateAsync(MemberAuthenticationSchemes.MembersCookie);
        if (!result.Succeeded || result.Principal is null)
        {
            return null;
        }

        var idValue = result.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idValue, out var memberId))
        {
            return null;
        }

        return await memberAccountService.FindByIdAsync(memberId, cancellationToken);
    }

    private static string? DescribeLinkError(string? linkError) => linkError switch
    {
        "provider" => ExternalLoginMessages.ProviderMismatch,
        "unverified" => ExternalLoginMessages.UnverifiedEmail,
        "suspended" => MemberAccountService.SuspendedSignInError,
        _ => null,
    };
}
