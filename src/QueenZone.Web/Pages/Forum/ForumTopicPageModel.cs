using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Web;

namespace QueenZone.Web.Pages.Forum;

public abstract class ForumTopicPageModel : PageModel
{
    private readonly IForumRepository forumRepository;
    private readonly ITopicWatchRepository topicWatchRepository;
    private readonly ForumOptions forumOptions;
    private readonly AdminOptions adminOptions;
    private readonly TimeProvider timeProvider;
    private readonly IForumPostReportRepository forumPostReportRepository;
    private readonly PrivateMessageService privateMessageService;

    protected ForumTopicPageModel(
        IForumRepository forumRepository,
        ITopicWatchRepository topicWatchRepository,
        IOptions<ForumOptions> forumOptions,
        IOptions<AdminOptions> adminOptions,
        TimeProvider timeProvider,
        IForumPostReportRepository forumPostReportRepository,
        PrivateMessageService privateMessageService)
    {
        this.forumRepository = forumRepository;
        this.topicWatchRepository = topicWatchRepository;
        this.forumOptions = forumOptions.Value;
        this.adminOptions = adminOptions.Value;
        this.timeProvider = timeProvider;
        this.forumPostReportRepository = forumPostReportRepository;
        this.privateMessageService = privateMessageService;
    }

    public ForumThreadHeader? Header { get; private set; }

    public IReadOnlyList<ForumPostViewModel> Posts { get; private set; } = [];

    public int CurrentPage { get; private set; }

    public int TotalPages { get; private set; }

    public int TotalPosts { get; private set; }

    public IReadOnlyList<BreadcrumbItem> Breadcrumbs { get; private set; } = [];

    public bool CanWatch { get; private set; }

    public bool IsWatching { get; private set; }

    public bool IsAdmin { get; private set; }

    protected AuthenticateResult? MemberAuth { get; set; }

    protected async Task<IActionResult> LoadTopicPageAsync(
        int topicId,
        string slug,
        int page,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            return NotFound();
        }

        var topicPage = await forumRepository.GetTopicPostsPageAsync(
            topicId,
            page,
            ForumRoutes.PostsPageSize,
            cancellationToken);
        if (topicPage is null)
        {
            return NotFound();
        }

        var header = PublicContentMapper.ToForumThreadHeader(topicPage.Header);
        var canonicalSlug = NewsSlug.Slugify(topicPage.Header.Title);
        if (!string.Equals(canonicalSlug, slug, StringComparison.OrdinalIgnoreCase))
        {
            return RedirectPermanent(ForumRoutes.GetTopicCanonicalPath(header, page));
        }

        var totalPages = ForumRoutes.GetPostsTotalPages(topicPage.TotalCount, ForumRoutes.PostsPageSize);
        if (topicPage.TotalCount == 0)
        {
            if (page > 1)
            {
                return NotFound();
            }
        }
        else if (page > totalPages)
        {
            return NotFound();
        }

        MemberAuth = await ResolveMemberAuthAsync();
        var memberId = ForumMember.GetMemberId(MemberAuth?.Principal);
        var isAdmin = await ForumAdminAccess.IsAdminAsync(HttpContext, adminOptions);
        IsAdmin = isAdmin;
        var utcNow = timeProvider.GetUtcNow();
        CanWatch = memberId is not null;
        IsWatching = memberId is not null
            && await topicWatchRepository.IsWatchingAsync(memberId.Value, topicId, cancellationToken);

        Header = header;
        Posts = topicPage.Posts
            .Select(PublicContentMapper.ToForumPostViewModel)
            .Select(post => PublicContentMapper.WithEditState(
                post,
                memberId,
                isAdmin,
                forumOptions.PostEditWindowMinutes,
                utcNow))
            .ToList();
        if (memberId is Guid signedInMemberId)
        {
            var postIds = Posts.Select(post => post.Id).ToArray();
            var reportedIds = await forumPostReportRepository.GetReportedPostIdsAsync(
                signedInMemberId, postIds, cancellationToken);
            var authorIds = Posts.Where(post => post.AuthorMemberId is not null)
                .Select(post => post.AuthorMemberId!.Value).Distinct().ToArray();
            var blockedIds = await privateMessageService.ListBlockedMemberIdsAsync(
                signedInMemberId, authorIds, cancellationToken);
            Posts = Posts.Select(post => post with
            {
                AlreadyReported = reportedIds.Contains(post.Id),
                IsBlocked = post.AuthorMemberId is Guid authorId && blockedIds.Contains(authorId),
            }).ToList();
        }
        CurrentPage = page;
        TotalPages = totalPages;
        TotalPosts = topicPage.TotalCount;
        Breadcrumbs =
        [
            BreadcrumbItem.Home,
            new BreadcrumbItem("Forum", "/forum"),
            new BreadcrumbItem(header.ForumName, header.CategoryPath),
            new BreadcrumbItem(header.Title, header.DetailPath),
        ];

        ViewData["Title"] = ForumRoutes.GetTopicPageTitle(header, page);
        ViewData["IsAdmin"] = IsAdmin;
        ViewData["CanonicalPath"] = ForumRoutes.GetTopicCanonicalPath(header, page);
        ViewData["Description"] = PageMetaDescription.ForForumTopic(
            Posts.Count > 0 ? Posts[0].Body : null,
            header.Title,
            header.ForumName,
            page);

        if (page > 1)
        {
            ViewData["PrevPath"] = ForumRoutes.GetTopicCanonicalPath(header, page - 1);
        }

        if (totalPages > 0 && page < totalPages)
        {
            ViewData["NextPath"] = ForumRoutes.GetTopicCanonicalPath(header, page + 1);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostWatchAsync(
        int topicId,
        string slug,
        CancellationToken cancellationToken) =>
        await ToggleWatchAsync(topicId, slug, watch: true, cancellationToken);

    public async Task<IActionResult> OnPostUnwatchAsync(
        int topicId,
        string slug,
        CancellationToken cancellationToken) =>
        await ToggleWatchAsync(topicId, slug, watch: false, cancellationToken);

    private async Task<IActionResult> ToggleWatchAsync(
        int topicId,
        string slug,
        bool watch,
        CancellationToken cancellationToken)
    {
        var memberId = await GetCurrentMemberIdAsync();
        if (memberId is null)
        {
            return Challenge(MemberAuthenticationSchemes.MembersCookie);
        }

        var page = Math.Max(CurrentPage, 1);
        var loadResult = await LoadTopicPageAsync(topicId, slug, page, cancellationToken);
        if (loadResult is not PageResult)
        {
            return loadResult;
        }

        if (watch)
        {
            await topicWatchRepository.WatchAsync(
                memberId.Value,
                topicId,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
        else
        {
            await topicWatchRepository.UnwatchAsync(memberId.Value, topicId, cancellationToken);
        }

        return Redirect(Header is null
            ? ForumRoutes.GetTopicCanonicalPath(topicId, slug, page)
            : ForumRoutes.GetTopicCanonicalPath(Header, page));
    }

    protected async Task<Guid?> GetCurrentMemberIdAsync()
    {
        MemberAuth ??= await ResolveMemberAuthAsync();
        return ForumMember.GetMemberId(MemberAuth?.Principal);
    }

    protected async Task<AuthenticateResult?> ResolveMemberAuthAsync()
    {
        var directId = ForumMember.GetMemberId(User);
        if (directId is not null)
        {
            return AuthenticateResult.Success(new AuthenticationTicket(User, MemberAuthenticationSchemes.MembersCookie));
        }

        var memberAuth = await HttpContext.AuthenticateMemberAsync();
        return memberAuth.Succeeded ? memberAuth : null;
    }
}
