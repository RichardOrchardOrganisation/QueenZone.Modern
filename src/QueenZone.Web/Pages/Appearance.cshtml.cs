using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using QueenZone.Web.Infrastructure;

namespace QueenZone.Web.Pages;

[EnableRateLimiting(QueenZoneRateLimitPolicies.AnonymousWrite)]
public sealed class AppearanceModel(IAntiforgery antiforgery) : PageModel
{
    [BindProperty]
    public DeviceThemeChoice DeviceTheme { get; set; } = DeviceThemeChoice.System;

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ViewData["Title"] = "Appearance | QueenZone";
        var auth = await HttpContext.AuthenticateMemberAsync();
        var saved = DeviceThemeCookie.Read(Request);
        DeviceTheme = saved is null && !auth.Succeeded ? DeviceThemeChoice.System : DeviceThemeCookie.ToChoice(saved);
    }

    public IActionResult OnGetToken()
    {
        Response.Headers.CacheControl = "no-store";
        return new JsonResult(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid || !Enum.IsDefined(DeviceTheme)) return BadRequest();
        DeviceThemeCookie.Write(HttpContext, DeviceTheme);
        Response.Headers.CacheControl = "no-store";
        return LocalRedirect(LocalReturnUrl.Resolve(ReturnUrl));
    }
}
