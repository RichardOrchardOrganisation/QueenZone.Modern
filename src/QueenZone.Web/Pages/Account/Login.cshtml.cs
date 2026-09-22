using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace QueenZone.Web.Pages.Account;

public sealed class LoginModel(
    IOptions<MemberAuthenticationOptions> memberAuthenticationOptions,
    MemberAccountService memberAccountService) : AccountPageModel(memberAuthenticationOptions)
{
    public string ReturnUrl { get; private set; } = "/";

    public bool ShowSignedOutMessage { get; private set; }

    public bool ShowSuspendedMessage { get; private set; }

    public bool ShowUnverifiedExternalEmail { get; private set; }

    public bool ShowExpiredExternalLink { get; private set; }

    [BindProperty]
    public PasswordSignInInput Input { get; set; } = new();

    public string? PasswordSignInError { get; private set; }

    public void OnGet(
        string? returnUrl,
        string? signedOut = null,
        string? suspended = null,
        string? externalEmail = null,
        string? externalLink = null)
    {
        ReturnUrl = ResolveReturnUrl(returnUrl);
        ShowSignedOutMessage = string.Equals(signedOut, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(signedOut, "true", StringComparison.OrdinalIgnoreCase);
        ShowSuspendedMessage = string.Equals(suspended, "1", StringComparison.OrdinalIgnoreCase);
        ShowUnverifiedExternalEmail = string.Equals(externalEmail, "unverified", StringComparison.OrdinalIgnoreCase);
        ShowExpiredExternalLink = string.Equals(externalLink, "expired", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Email/password fallback for sign-in when a social provider is unavailable (e.g. an App
    /// Store/Play Store reviewer). There is no self-service registration for this path — accounts
    /// are provisioned out-of-band (see QueenZone.Tools create-reviewer-account).
    /// </summary>
    public async Task<IActionResult> OnPostAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        ReturnUrl = ResolveReturnUrl(returnUrl);

        if (!ModelState.IsValid)
        {
            PasswordSignInError = "Enter your email and password.";
            return Page();
        }

        var result = await memberAccountService.SignInAsync(Input.Email, Input.Password, cancellationToken);
        if (!result.Succeeded || result.Account is null)
        {
            PasswordSignInError = result.Error ?? "Incorrect email or password.";
            return Page();
        }

        await MemberCookieSignIn.SignInAsync(HttpContext, result.Account);

        return Redirect(ReturnUrl);
    }

    public sealed class PasswordSignInInput
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }
}
