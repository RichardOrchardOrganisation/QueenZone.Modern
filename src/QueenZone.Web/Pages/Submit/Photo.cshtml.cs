using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class PhotoModel(
    PhotoSubmissionService photoSubmissionService,
    IPhotoRepository photoRepository) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, ErrorMessage = "Title must be 200 characters or fewer.")]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(1000, ErrorMessage = "Description must be 1000 characters or fewer.")]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [BindProperty]
    [StringLength(100, ErrorMessage = "Suggested category must be 100 characters or fewer.")]
    [Display(Name = "Suggested category")]
    public string? SuggestedCategory { get; set; }

    [BindProperty]
    [Range(1900, 2100, ErrorMessage = "Year must be between 1900 and 2100.")]
    [Display(Name = "Approximate year")]
    public int? ApproximateYear { get; set; }

    [BindProperty]
    [Display(Name = "Approximate date")]
    [DataType(DataType.Date)]
    public DateOnly? ApproximateDate { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Choose a photo to upload.")]
    [Display(Name = "Photo")]
    public IFormFile? PhotoFile { get; set; }

    public IReadOnlyList<PhotoCategory> Categories { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (await HttpContext.AuthenticateMemberIdAsync() is null)
        {
            return Redirect("/account/login");
        }

        Categories = await photoRepository.GetCategoriesAsync(cancellationToken);
        ViewData["Title"] = "Submit a photo";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        Categories = await photoRepository.GetCategoriesAsync(cancellationToken);
        ViewData["Title"] = "Submit a photo";

        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (PhotoFile is null || PhotoFile.Length <= 0)
        {
            ModelState.AddModelError(nameof(PhotoFile), "Choose a photo to upload.");
            return Page();
        }

        await using var stream = PhotoFile.OpenReadStream();
        var result = await photoSubmissionService.SubmitAsync(
            memberId.Value,
            Title,
            Description,
            SuggestedCategory,
            ApproximateYear,
            ApproximateDate,
            stream,
            PhotoFile.FileName,
            cancellationToken);

        if (!result.Succeeded || result.Submission is null)
        {
            ModelState.AddModelError(nameof(PhotoFile), result.Error ?? "Could not submit photo.");
            return Page();
        }

        return Redirect($"/submit/photo/confirmation/{result.Submission.Id:D}");
    }
}
