using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Forum;

[Authorize(Policy = MemberAuthenticationSchemes.MemberPolicy)]
[RequestFormLimits(MultipartBodyLengthLimit = 55 * 1024 * 1024)]
[RequestSizeLimit(55 * 1024 * 1024)]
public sealed class NewThreadModel(
    IForumRepository forumRepository,
    ForumPostWriteService forumPostWrite) : PageModel
{
    [BindProperty]
    [Required]
    [StringLength(ForumPostWriteService.SubjectMaxLength, MinimumLength = ForumPostWriteService.SubjectMinLength)]
    public string Subject { get; set; } = string.Empty;

    [BindProperty]
    [Required]
    public string Body { get; set; } = string.Empty;

    [BindProperty]
    public List<IFormFile> Attachments { get; set; } = [];

    [BindProperty]
    public ForumPollForm Poll { get; set; } = new();

    public ForumCategorySummary? Category { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(string categorySlug, CancellationToken cancellationToken)
    {
        var category = await ResolveCategoryAsync(categorySlug, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        PopulatePage(category);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string categorySlug, CancellationToken cancellationToken)
    {
        var category = await ResolveCategoryAsync(categorySlug, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        PopulatePage(category);

        var memberId = ForumMember.GetMemberId(User);
        if (memberId is null)
        {
            return Challenge();
        }

        var pollErrors = new List<string>();
        var newPoll = (Poll ?? new ForumPollForm()).ToNewPoll(memberId.Value, pollErrors);
        foreach (var error in pollErrors)
        {
            ModelState.AddModelError(nameof(Poll), error);
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var outcome = await forumPostWrite.CreateTopicAsync(new NewForumTopicRequest(memberId.Value, User.Identity?.Name, category.Id, Subject, Body, Attachments, newPoll), cancellationToken);

        return MapOutcome(outcome);
    }

    private IActionResult MapOutcome(ForumWriteOutcome outcome)
    {
        Body = outcome.SanitizedBody;
        switch (outcome.Status)
        {
            case ForumWriteStatus.Success:
                return Redirect(ForumRoutes.GetTopicCanonicalPath(outcome.TopicId, outcome.Title));
            case ForumWriteStatus.CategoryNotFound:
                return NotFound();
            case ForumWriteStatus.RateLimited:
                return StatusCode(StatusCodes.Status429TooManyRequests);
            case ForumWriteStatus.MemberSuspended:
                return Challenge();
            case ForumWriteStatus.ValidationFailed:
            case ForumWriteStatus.AttachmentFailed:
                foreach (var error in outcome.FieldErrors)
                {
                    ModelState.AddModelError(error.Field, error.Message);
                }

                return Page();
            default:
                return Page();
        }
    }

    private async Task<ForumCategorySummary?> ResolveCategoryAsync(string categorySlug, CancellationToken cancellationToken)
    {
        var categories = await forumRepository.GetCategoriesAsync(cancellationToken);
        var category = categories.FirstOrDefault(item =>
            string.Equals(NewsSlug.Slugify(item.Name), categorySlug, StringComparison.OrdinalIgnoreCase));
        return category is null ? null : PublicContentMapper.ToForumCategorySummary(category);
    }

    private void PopulatePage(ForumCategorySummary category)
    {
        Category = category;
        Breadcrumbs =
        [
            BreadcrumbItem.Home,
            new BreadcrumbItem("Forum", "/forum"),
            new BreadcrumbItem(category.Name, category.DetailPath),
            new BreadcrumbItem("New thread", ForumRoutes.GetNewThreadPath(category)),
        ];
        ViewData["Title"] = $"New thread | {category.Name} | QueenZone forum";
        ViewData["CanonicalPath"] = ForumRoutes.GetNewThreadPath(category);
    }
}
