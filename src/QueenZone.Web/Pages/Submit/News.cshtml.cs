using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace QueenZone.Web.Pages.Submit;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy, AuthenticationSchemes = MemberAuthenticationSchemes.MembersCookie)]
public sealed class NewsModel(NewsSuggestionService newsSuggestionService) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "URL is required.")]
    [StringLength(2000, ErrorMessage = "URL must be 2000 characters or fewer.")]
    [Display(Name = "News story URL")]
    public string StoryUrl { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(300, ErrorMessage = "Suggested headline must be 300 characters or fewer.")]
    [Display(Name = "Suggested headline")]
    public string? Title { get; set; }

    [BindProperty]
    [StringLength(1000, ErrorMessage = "Notes must be 1000 characters or fewer.")]
    [Display(Name = "Notes for the editor")]
    public string? Notes { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (await HttpContext.AuthenticateMemberIdAsync() is null)
        {
            return Redirect("/account/login");
        }

        ViewData["Title"] = "Suggest news";
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var memberId = await HttpContext.AuthenticateMemberIdAsync();
        if (memberId is null)
        {
            return Redirect("/account/login");
        }

        ViewData["Title"] = "Suggest news";

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var outcome = await newsSuggestionService.SubmitAsync(
            memberId.Value,
            StoryUrl,
            Title,
            Notes,
            cancellationToken);

        // Records synthesize a copy constructor, so CS8509 cannot see the nested sum as closed.
#pragma warning disable CS8509
        return outcome switch
        {
            SubmitOutcome.Accepted => Redirect("/submit/news/confirmation"),
            SubmitOutcome.InvalidField
                or SubmitOutcome.DuplicateActive
                or SubmitOutcome.DailyLimit
                or SubmitOutcome.SignInRequired => FailurePage(outcome.Message),
        };
#pragma warning restore CS8509

        IActionResult FailurePage(string message)
        {
            ModelState.AddModelError(
                string.Empty,
                string.IsNullOrEmpty(message) ? "Could not submit suggestion." : message);
            return Page();
        }
    }
}
