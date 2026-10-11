using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class PhotoCorrectionModel(
    IPhotoCorrectionRepository photoCorrectionRepository,
    IAdminPhotoRepository photoRepository) : PageModel
{
    [BindProperty]
    [Display(Name = "What needs correcting")]
    public string Kind { get; set; } = PhotoCorrectionKind.Caption;

    [BindProperty]
    [Display(Name = "Corrected value")]
    public string ProposedValue { get; set; } = string.Empty;

    [BindProperty]
    [Display(Name = "How do you know?")]
    public string Explanation { get; set; } = string.Empty;

    [BindProperty]
    [Display(Name = "Evidence link")]
    public string? EvidenceUrl { get; set; }

    [BindProperty]
    [Display(Name = "Evidence type")]
    public string? EvidenceType { get; set; }

    [BindProperty]
    [Display(Name = "How sure are you?")]
    public string Confidence { get; set; } = PhotoCorrectionConfidence.Probable;

    [BindProperty]
    [Display(Name = "Name to credit publicly")]
    public string? PublicCreditName { get; set; }

    /// <summary>Client-generated id so a double submit or retry cannot create two proposals.</summary>
    [BindProperty]
    public Guid SubmissionId { get; set; } = Guid.NewGuid();

    public AdminPhotoItem? Photo { get; private set; }

    public async Task<IActionResult> OnGetAsync(int picId, CancellationToken cancellationToken)
    {
        if (await HttpContext.AuthenticateMemberIdAsync() is null)
        {
            return Redirect("/account/login");
        }

        return await LoadPhotoAsync(picId, cancellationToken) ?? Page();
    }

    public async Task<IActionResult> OnPostAsync(int picId, CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        if (await LoadPhotoAsync(picId, cancellationToken) is { } notFound)
        {
            return notFound;
        }

        var errors = PhotoCorrectionValidation.Validate(
            Kind,
            ProposedValue,
            Explanation,
            EvidenceUrl,
            EvidenceType,
            Confidence,
            PublicCreditName);
        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var created = await photoCorrectionRepository.CreateAsync(
                new NewPhotoCorrectionProposal(
                    picId,
                    memberId.Value,
                    Kind,
                    CurrentValueFor(Kind, Photo!),
                    ProposedValue,
                    Explanation,
                    EvidenceUrl,
                    EvidenceType,
                    Confidence,
                    PublicCreditName,
                    SubmissionId),
                cancellationToken);
            return Redirect($"/submit/photo-correction/confirmation/{created.Id:D}");
        }
        catch (InvalidOperationException ex) when (ex is DuplicatePhotoCorrectionException or PhotoCorrectionLimitException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
    }

    internal static string? CurrentValueFor(string kind, AdminPhotoItem photo) =>
        kind == PhotoCorrectionKind.Caption ? photo.Title : null;

    /// <summary>Only visible (published) photos can receive proposals; anything else is a 404.</summary>
    private async Task<IActionResult?> LoadPhotoAsync(int picId, CancellationToken cancellationToken)
    {
        var photo = await photoRepository.GetByIdAsync(picId, cancellationToken);
        if (photo is null || !photo.IsVisible)
        {
            return NotFound();
        }

        Photo = photo;
        ViewData["Title"] = "Suggest a correction";
        return null;
    }
}
