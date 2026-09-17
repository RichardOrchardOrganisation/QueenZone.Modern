using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class QuizQuestionModel(IQuizQuestionSubmissionRepository quizQuestionSubmissionRepository) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Question text is required.")]
    [StringLength(QuizValidation.QuestionMaxLength, ErrorMessage = "Question text must be 500 characters or fewer.")]
    [Display(Name = "Question")]
    public string QuestionText { get; set; } = string.Empty;

    [BindProperty]
    public List<string> OptionTexts { get; set; } = ["", "", "", ""];

    [BindProperty]
    [Display(Name = "Correct option")]
    public int CorrectOptionIndex { get; set; }

    [BindProperty]
    [StringLength(QuizQuestionSubmissionValidation.MaxSourceNoteLength, ErrorMessage = "Explanation or source note must be 1000 characters or fewer.")]
    [Display(Name = "Explanation or source note")]
    public string? SourceNote { get; set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await GetCurrentMemberIdAsync() is null)
        {
            return Redirect("/account/login");
        }

        ViewData["Title"] = "Suggest a quiz question";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var memberId = await GetCurrentMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        ViewData["Title"] = "Suggest a quiz question";

        var options = OptionTexts
            .Select((text, index) => new QuizQuestionSubmissionOptionDraft(text ?? string.Empty, index == CorrectOptionIndex))
            .ToList();
        var sourceNote = string.IsNullOrWhiteSpace(SourceNote) ? null : SourceNote.Trim();

        var errors = QuizQuestionSubmissionValidation.ValidateSubmission(QuestionText, options, sourceNote);
        if (errors.Count > 0)
        {
            Errors = errors;
            return Page();
        }

        var created = await quizQuestionSubmissionRepository.CreateAsync(
            new NewQuizQuestionSubmission(memberId.Value, QuestionText.Trim(), options, sourceNote),
            cancellationToken);

        return Redirect($"/submit/quiz-question/confirmation/{created.Id:D}");
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
