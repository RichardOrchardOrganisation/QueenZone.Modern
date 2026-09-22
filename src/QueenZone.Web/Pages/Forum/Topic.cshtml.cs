using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using QueenZone.Data;

namespace QueenZone.Web.Pages.Forum;

[RequestFormLimits(MultipartBodyLengthLimit = 55 * 1024 * 1024)]
[RequestSizeLimit(55 * 1024 * 1024)]
public sealed class TopicModel : ForumTopicPageModel
{
    private readonly IForumRepository forumRepository;
    private readonly IForumPollRepository forumPollRepository;
    private readonly ForumPostWriteService forumPostWrite;

    public TopicModel(
        IForumRepository forumRepository,
        IForumPollRepository forumPollRepository,
        ForumPostWriteService forumPostWrite,
        IOptions<AdminOptions> adminOptions,
        IOptions<ForumOptions> forumOptions,
        ITopicWatchRepository topicWatchRepository,
        TimeProvider timeProvider,
        IForumPostReportRepository forumPostReportRepository,
        PrivateMessageService privateMessageService)
        : base(forumRepository, topicWatchRepository, forumOptions, adminOptions, timeProvider,
            forumPostReportRepository, privateMessageService)
    {
        this.forumRepository = forumRepository;
        this.forumPollRepository = forumPollRepository;
        this.forumPostWrite = forumPostWrite;
    }

    [BindProperty]
    [Required]
    public string Body { get; set; } = string.Empty;

    [BindProperty]
    public List<IFormFile> Attachments { get; set; } = [];

    public bool CanReply { get; private set; }

    public ForumPollResults? Poll { get; private set; }

    public async Task<IActionResult> OnGetAsync(int topicId, string slug, CancellationToken cancellationToken)
    {
        var result = await LoadTopicPageAsync(topicId, slug, 1, cancellationToken);
        CanReply = await GetCurrentMemberIdAsync() is not null;
        if (result is PageResult)
        {
            await LoadPollAsync(topicId, cancellationToken);
        }

        return result;
    }

    public async Task<IActionResult> OnPostAsync(int topicId, string slug, CancellationToken cancellationToken)
    {
        var memberId = await GetCurrentMemberIdAsync();
        if (memberId is null)
        {
            return Challenge(MemberAuthenticationSchemes.MembersCookie);
        }

        var loadResult = await LoadTopicPageAsync(topicId, slug, 1, cancellationToken);
        if (loadResult is not PageResult)
        {
            return loadResult;
        }
        CanReply = true;
        await LoadPollAsync(topicId, cancellationToken);

        var outcome = await forumPostWrite.CreateReplyAsync(
            memberId.Value,
            User.Identity?.Name,
            topicId,
            Body,
            Attachments,
            cancellationToken);

        return await MapOutcomeAsync(outcome, topicId, slug, cancellationToken);
    }

    private async Task<IActionResult> MapOutcomeAsync(
        ForumWriteOutcome outcome,
        int topicId,
        string slug,
        CancellationToken cancellationToken)
    {
        Body = outcome.SanitizedBody;
        switch (outcome.Status)
        {
            case ForumWriteStatus.Success:
                var updatedPage = await forumRepository.GetTopicPostsPageAsync(topicId, 1, 1, cancellationToken);
                var lastPage = updatedPage is null
                    ? 1
                    : Math.Max(1, ForumRoutes.GetPostsTotalPages(updatedPage.TotalCount, ForumRoutes.PostsPageSize));
                var redirectPath = Header is null
                    ? ForumRoutes.GetTopicCanonicalPath(topicId, slug, lastPage)
                    : ForumRoutes.GetTopicCanonicalPath(Header, lastPage);
                return Redirect(redirectPath + $"#post-{outcome.PostId}");
            case ForumWriteStatus.TopicNotFound:
                return NotFound();
            case ForumWriteStatus.TopicLocked:
                return Forbid();
            case ForumWriteStatus.RateLimited:
                return StatusCode(StatusCodes.Status429TooManyRequests);
            case ForumWriteStatus.MemberSuspended:
                return Challenge(MemberAuthenticationSchemes.MembersCookie);
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

    private async Task LoadPollAsync(int topicId, CancellationToken cancellationToken)
    {
        // HasPoll == false: skip the poll round-trip. null means unknown (legacy/sample) — load defensively.
        if (Header?.HasPoll == false)
        {
            Poll = null;
            return;
        }

        MemberAuth ??= await ResolveMemberAuthAsync();
        var memberId = ForumMember.GetMemberId(MemberAuth?.Principal);
        // Same admin-scheme check as hide/edit. An Entra session has no member principal.
        var isAdmin = IsAdmin;
        Poll = await forumPollRepository.GetPollWithResultsAsync(
            topicId,
            memberId,
            isAdmin,
            cancellationToken);
    }
}
