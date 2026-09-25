using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
[RequestFormLimits(MultipartBodyLengthLimit = 28_000_000)]
[RequestSizeLimit(28_000_000)]
public sealed class FanPerformanceModel(FanPerformanceSubmissionService fanPerformanceSubmissionService) : PageModel
{
    public const string RightsDeclarationCopy =
        "I confirm this recording is my own performance of a Queen song and I agree to it being published on QueenZone.";

    [BindProperty]
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, ErrorMessage = "Title must be 200 characters or fewer.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Covered song is required.")]
    [StringLength(200, ErrorMessage = "Covered song must be 200 characters or fewer.")]
    [Display(Name = "Queen song covered")]
    public string CoveredSong { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Performed by is required.")]
    [StringLength(200, ErrorMessage = "Performed by must be 200 characters or fewer.")]
    [Display(Name = "Performed by")]
    public string PerformedBy { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(2000, ErrorMessage = "Description must be 2000 characters or fewer.")]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Choose an audio file to upload.")]
    [Display(Name = "Audio file")]
    public IFormFile? AudioFile { get; set; }

    [BindProperty]
    [Range(typeof(bool), "true", "true", ErrorMessage = "You must confirm this is your own performance and agree to publication.")]
    [Display(Name = "Rights declaration")]
    public bool RightsDeclarationAccepted { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await HttpContext.AuthenticateMemberIdAsync() is null)
        {
            return Redirect("/account/login");
        }

        ViewData["Title"] = "Submit a fan performance";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        ViewData["Title"] = "Submit a fan performance";

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (AudioFile is null || AudioFile.Length <= 0)
        {
            ModelState.AddModelError(nameof(AudioFile), "Choose an audio file to upload.");
            return Page();
        }

        if (!RightsDeclarationAccepted)
        {
            ModelState.AddModelError(
                nameof(RightsDeclarationAccepted),
                "You must confirm this is your own performance and agree to publication.");
            return Page();
        }

        await using var stream = AudioFile.OpenReadStream();
        var result = await fanPerformanceSubmissionService.SubmitAsync(
            memberId.Value,
            Title,
            CoveredSong,
            PerformedBy,
            Description,
            RightsDeclarationAccepted,
            stream,
            AudioFile.FileName,
            cancellationToken);

        if (!result.Succeeded || result.Submission is null)
        {
            ModelState.AddModelError(nameof(AudioFile), result.Error ?? "Could not submit fan performance.");
            return Page();
        }

        return Redirect($"/submit/fan-performance/confirmation/{result.Submission.Id:D}");
    }
}
