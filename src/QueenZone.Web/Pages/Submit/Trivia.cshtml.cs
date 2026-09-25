using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class TriviaModel(ITriviaFactSubmissionRepository triviaFactSubmissionRepository) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Fact text is required.")]
    [StringLength(TriviaValidation.MaxTextLength, ErrorMessage = "Fact text must be 1000 characters or fewer.")]
    [Display(Name = "Trivia fact")]
    public string Text { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(TriviaValidation.MaxCategoryLength, ErrorMessage = "Category must be 100 characters or fewer.")]
    [Display(Name = "Category")]
    public string? Category { get; set; }

    [BindProperty]
    [Display(Name = "Difficulty")]
    public string? Difficulty { get; set; }

    [BindProperty]
    [StringLength(TriviaValidation.MaxSourceNoteLength, ErrorMessage = "Source or context note must be 1000 characters or fewer.")]
    [Display(Name = "Source or context")]
    public string? SourceNote { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await HttpContext.AuthenticateMemberIdAsync() is null)
        {
            return Redirect("/account/login");
        }

        ViewData["Title"] = "Suggest a trivia fact";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        ViewData["Title"] = "Suggest a trivia fact";

        var text = (Text ?? string.Empty).Trim();
        var category = NormalizeOptional(Category);
        var difficulty = NormalizeDifficulty(Difficulty);
        var sourceNote = NormalizeOptional(SourceNote);

        foreach (var error in TriviaValidation.ValidateSuggestion(text, category, difficulty, sourceNote))
        {
            ModelState.AddModelError(string.Empty, error);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var created = await triviaFactSubmissionRepository.CreateAsync(
            new NewTriviaFactSubmission(memberId.Value, text, category, difficulty, sourceNote),
            cancellationToken);

        return Redirect($"/submit/trivia/confirmation/{created.Id:D}");
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeDifficulty(string? value)
    {
        var trimmed = NormalizeOptional(value);
        return trimmed?.ToLowerInvariant();
    }
}
