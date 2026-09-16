using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
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

    [BindProperty]
    public PasswordSignInInput Input { get; set; } = new();

    public string? PasswordSignInError { get; private set; }

    public void OnGet(string? returnUrl, string? signedOut = null, string? suspended = null)
    {
        ReturnUrl = ResolveReturnUrl(returnUrl);
        ShowSignedOutMessage = string.Equals(signedOut, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(signedOut, "true", StringComparison.OrdinalIgnoreCase);
        ShowSuspendedMessage = string.Equals(suspended, "1", StringComparison.OrdinalIgnoreCase);
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

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, result.Account.Id.ToString()),
            new Claim(ClaimTypes.Email, result.Account.Email),
            new Claim(ClaimTypes.Name, result.Account.DisplayName),
        };
        var identity = new ClaimsIdentity(claims, MemberAuthenticationSchemes.MembersCookie);
        await HttpContext.SignInAsync(MemberAuthenticationSchemes.MembersCookie, new ClaimsPrincipal(identity));

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
