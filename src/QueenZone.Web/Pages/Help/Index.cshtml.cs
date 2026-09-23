using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Help;

public sealed class IndexModel(HelpRequestService helpRequestService) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Please choose a topic.")]
    [Display(Name = "Topic")]
    public string Topic { get; set; } = HelpRequestTopic.Other;

    [BindProperty]
    [Required(ErrorMessage = "Subject is required.")]
    [StringLength(HelpRequestService.MaxSubjectLength, MinimumLength = HelpRequestService.MinSubjectLength)]
    [Display(Name = "Subject")]
    public string Subject { get; set; } = string.Empty;

    [BindProperty]
    [Required(ErrorMessage = "Message is required.")]
    [StringLength(HelpRequestService.MaxMessageLength, MinimumLength = HelpRequestService.MinMessageLength)]
    [Display(Name = "Your message")]
    public string Message { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(HelpRequestService.MaxNameLength)]
    [Display(Name = "Your name")]
    public string? Name { get; set; }

    [BindProperty]
    [StringLength(HelpRequestService.MaxEmailLength)]
    [EmailAddress(ErrorMessage = "Enter a valid email address.")]
    [Display(Name = "Email address")]
    public string? Email { get; set; }

    [BindProperty]
    [Display(Name = "Website")]
    public string? Website { get; set; }

    [BindProperty]
    public string? FormStamp { get; set; }

    public bool IsSignedIn { get; private set; }

    public string? SignedInDisplayName { get; private set; }

    public IReadOnlyList<(string Value, string Label)> Topics { get; } =
    [
        (HelpRequestTopic.Account, HelpRequestTopic.DisplayName(HelpRequestTopic.Account)),
        (HelpRequestTopic.Content, HelpRequestTopic.DisplayName(HelpRequestTopic.Content)),
        (HelpRequestTopic.Technical, HelpRequestTopic.DisplayName(HelpRequestTopic.Technical)),
        (HelpRequestTopic.Privacy, HelpRequestTopic.DisplayName(HelpRequestTopic.Privacy)),
        (HelpRequestTopic.Other, HelpRequestTopic.DisplayName(HelpRequestTopic.Other)),
    ];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await PopulateSignedInStateAsync();
        FormStamp = helpRequestService.IssueFormStamp();
        ViewData["Title"] = "Contact — Queenzone";
        ViewData["Description"] = "Contact the Queenzone site admin if you have a problem with your account or the archive.";
        ViewData["CanonicalPath"] = "/contact";
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var memberId = await PopulateSignedInStateAsync();
        ViewData["Title"] = "Contact — Queenzone";
        ViewData["Description"] = "Contact the Queenzone site admin if you have a problem with your account or the archive.";
        ViewData["CanonicalPath"] = "/contact";

        if (!IsSignedIn)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                ModelState.AddModelError(nameof(Name), "Name is required.");
            }

            if (string.IsNullOrWhiteSpace(Email))
            {
                ModelState.AddModelError(nameof(Email), "Email address is required.");
            }
        }

        if (!ModelState.IsValid)
        {
            FormStamp ??= helpRequestService.IssueFormStamp();
            return Page();
        }

        var result = await helpRequestService.SubmitAsync(
            memberId,
            Topic,
            Subject,
            Message,
            Name,
            Email,
            Website,
            FormStamp,
            HelpRequestService.ResolveClientIp(HttpContext),
            cancellationToken);

        if (result.SilentlyDropped || result.Succeeded)
        {
            return Redirect("/contact/confirmation");
        }

        if (result.Error?.StartsWith("Too many", StringComparison.OrdinalIgnoreCase) == true)
        {
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
        }

        ModelState.AddModelError(string.Empty, result.Error ?? "Could not send your message.");
        FormStamp = helpRequestService.IssueFormStamp();
        return Page();
    }

    private async Task<Guid?> PopulateSignedInStateAsync()
    {
        var authResult = await HttpContext.AuthenticateMemberAsync();
        if (!authResult.Succeeded || authResult.Principal is null)
        {
            IsSignedIn = false;
            return null;
        }

        var idValue = authResult.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idValue, out var memberId))
        {
            IsSignedIn = false;
            return null;
        }

        IsSignedIn = true;
        SignedInDisplayName = authResult.Principal.FindFirstValue(ClaimTypes.Name);
        return memberId;
    }
}
