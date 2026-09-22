using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using QueenZone.Data;

namespace QueenZone.Web.Pages.FanPerformances;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
[EnableRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)]
public sealed class ReportModel(
    IFanPerformanceRepository fanPerformanceRepository,
    FanPerformanceReportService fanPerformanceReportService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public FanPerformance? Performance { get; private set; }

    [BindProperty]
    [Required(ErrorMessage = "A reason is required.")]
    [StringLength(FanPerformanceReportLimits.MaxReasonLength, ErrorMessage = "Reason must be 1000 characters or fewer.")]
    [Display(Name = "Reason")]
    public string Reason { get; set; } = string.Empty;

    public string? StatusMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await GetCurrentMemberIdAsync() is null)
        {
            return Redirect(FanPerformanceRoutes.GetLoginPath(FanPerformanceRoutes.GetReportPath(Id)));
        }

        Performance = await fanPerformanceRepository.GetByIdAsync(Id, cancellationToken);
        if (Performance is null)
        {
            return NotFound();
        }

        ViewData["Title"] = "Report a fan performance";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var memberId = await GetCurrentMemberIdAsync();
        if (memberId is null)
        {
            return Redirect(FanPerformanceRoutes.GetLoginPath(FanPerformanceRoutes.GetReportPath(Id)));
        }

        Performance = await fanPerformanceRepository.GetByIdAsync(Id, cancellationToken);
        if (Performance is null)
        {
            return NotFound();
        }

        ViewData["Title"] = "Report a fan performance";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await fanPerformanceReportService.CreateAsync(
            memberId.Value,
            Id,
            Reason,
            cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(Reason), result.Error ?? "Could not send the report.");
            return Page();
        }

        StatusMessage = result.AlreadyReported
            ? "You have already reported this performance. The admin team still has your open report."
            : "Thanks. The admin team will review this performance.";
        return Page();
    }

    private async Task<Guid?> GetCurrentMemberIdAsync()
    {
        var authResult = await HttpContext.AuthenticateMemberAsync();
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            return null;
        }

        var idValue = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idValue, out var id) ? id : null;
    }
}
