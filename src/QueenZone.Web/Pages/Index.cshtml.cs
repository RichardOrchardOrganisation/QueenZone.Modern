using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QueenZone.Data;

namespace QueenZone.Web.Pages;

public sealed class IndexModel(
    PublicQueryCacheService publicQueryCache,
    NewsDiscussionComposer newsDiscussion,
    IHomePollRepository homePollRepository,
    HomePollVoteService homePollVoteService,
    QuizSprintService quizSprintService,
    TimeProvider timeProvider,
    ILogger<IndexModel> logger) : PageModel
{
    /// <summary>Stock archive images cycled deterministically per article, since legacy
    /// article rows carry no per-item image (see <see cref="ArticleItem"/>).</summary>
    private static readonly string[] FeaturedArticleImages =
    [
        "/design-system/assets/img-studio.jpg",
        "/design-system/assets/img-portrait.jpg",
        "/design-system/assets/img-crowd.jpg",
        "/design-system/assets/img-stage.jpg",
    ];

    private const int HomeNewsCount = 8;

    /// <summary>One feature tile plus five, filling the 3×3 front-page photo grid.</summary>
    private const int LatestPhotoCount = 6;

    /// <summary>Threads in the above-the-fold "Forum now" card; the rest fill the forum band.</summary>
    public const int ForumNowCount = 5;

    private const int ForumBandCount = 6;

    public IReadOnlyList<NewsArchiveItem> Latest { get; private set; } = [];

    public IReadOnlyList<ForumRecentThreadSummary> ForumThreads { get; private set; } = [];

    public IReadOnlyList<ForumRecentThreadSummary> ForumNow { get; private set; } = [];

    public IReadOnlyList<ForumRecentThreadSummary> ForumBand { get; private set; } = [];

    /// <summary>Forum replies posted today, or null when the count could not be loaded.</summary>
    public int? ForumRepliesToday { get; private set; }

    public IReadOnlyList<PhotoItem> LatestPhotos { get; private set; } = [];

    public IReadOnlyList<HomeTickerItem> Ticker { get; private set; } = [];

    public DateTimeOffset Now { get; private set; }

    public IReadOnlyList<QueenHistoryEvent> OnThisDay { get; private set; } = [];

    public bool IsOnThisDayFallback { get; private set; }

    public IReadOnlyList<HomeArticleTeaser> FeaturedArticles { get; private set; } = [];

    public QuoteItem? FeaturedQuote { get; private set; }

    public SprintBoard SprintBoard { get; private set; } = new([], null, 0);

    public HomePollResults? HomePoll { get; private set; }

    public bool HomePollViewerCanVote { get; private set; }

    public string? HomePollError { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "QueenZone";
        ViewData["Description"] = "The complete fan resource for Queen – the latest news, forum, photos, articles, daily quiz and poll, plus the Queenzone.com archive.";
        ViewData["CanonicalPath"] = "/";
        try
        {
            SprintBoard = await quizSprintService.GetBoardAsync(null, 3, cancellationToken);
        }
        catch (Exception exception)
        {
            // Optional chrome: a missing QuizSprintRuns table or other SQL/schema failure on
            // the RealData Express mirror must not take down GET / (empty board is still 200).
            logger.LogWarning(exception, "Homepage quiz sprint board failed to load.");
            SprintBoard = new([], null, 0);
        }

        Now = timeProvider.GetUtcNow();
        var latest = await publicQueryCache.GetLatestNewsAsync(HomeNewsCount, cancellationToken);
        Latest = await newsDiscussion.ToArchiveItemsAsync(latest, cancellationToken);
        var threads = await publicQueryCache.GetForumRecentThreadsAsync(ForumRoutes.RecentThreadsCount, cancellationToken);
        ForumThreads = PublicContentMapper.ToForumRecentThreadSummaries(threads);
        ForumNow = ForumThreads.Take(ForumNowCount).ToList();
        ForumBand = ForumThreads.Skip(ForumNowCount).Take(ForumBandCount).ToList();
        ForumRepliesToday = await LoadForumRepliesTodayAsync(cancellationToken);
        LatestPhotos = await publicQueryCache.GetLatestPhotosAsync(LatestPhotoCount, cancellationToken);
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        OnThisDay = await publicQueryCache.GetOnThisDayAsync(today, 3, cancellationToken);

        if (OnThisDay.Count == 0)
        {
            OnThisDay = await publicQueryCache.GetAroundThisDayAsync(today, 7, 3, cancellationToken);
            IsOnThisDayFallback = OnThisDay.Count > 0;
        }

        var articles = await publicQueryCache.GetLatestArticlesAsync(ArticlesRoutes.HomeFeaturedCount, cancellationToken);
        var community = await publicQueryCache.GetLatestCommunityArticlesAsync(
            ArticlesRoutes.HomeFeaturedCount,
            cancellationToken);
        var teasers = articles.Select(item => new
        {
            item.PublishedAt,
            item.Title,
            item.Excerpt,
            Category = string.IsNullOrWhiteSpace(item.CategoryName) ? "Feature" : item.CategoryName,
            Image = NewsArticleImage.ResolveImageUrl(item.ImageBlobKey, null),
            Href = ArticlesRoutes.GetArticleDetailPath(item),
        }).Concat(community.Select(item => new
        {
            PublishedAt = item.PublishedAt.UtcDateTime,
            item.Title,
            Excerpt = item.Excerpt ?? string.Empty,
            Category = string.IsNullOrWhiteSpace(item.Category) ? "Feature" : item.Category,
            Image = NewsArticleImage.ResolveImageUrl(item.CoverImageBlobPath, null),
            Href = ArticlesRoutes.GetCommunityArticleDetailPath(item.Slug),
        })).OrderByDescending(item => item.PublishedAt).Take(ArticlesRoutes.HomeFeaturedCount).ToList();
        FeaturedArticles = teasers
            .Select((item, index) => new HomeArticleTeaser(
                item.Image ?? FeaturedArticleImages[index % FeaturedArticleImages.Length],
                item.Category,
                item.Title,
                item.Excerpt,
                item.PublishedAt.ToString("dd MMMM yyyy"),
                item.Href))
            .ToList();

        FeaturedQuote = await publicQueryCache.GetRandomPublishedQuoteAsync(cancellationToken);
        await LoadHomePollAsync(cancellationToken);
        HomePollError = TempData["HomePollError"] as string;
        Ticker = BuildTicker();
    }

    public async Task<IActionResult> OnPostVoteAsync(Guid optionId, CancellationToken cancellationToken)
    {
        var memberAuth = await HttpContext.AuthenticateMemberAsync();
        var memberId = ForumMember.GetMemberId(memberAuth.Principal);
        if (memberId is null)
        {
            return Unauthorized();
        }

        try
        {
            await homePollVoteService.CastVoteAsync(memberId.Value, optionId, cancellationToken);
        }
        catch (ForumPollVoteException ex)
        {
            TempData["HomePollError"] = ex.Message;
        }

        return Redirect("/#home-poll");
    }

    private async Task<int?> LoadForumRepliesTodayAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await publicQueryCache.GetLiveActivityNewForumRepliesTodayAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // Optional chrome, like the sprint board: the strip simply drops the count.
            logger.LogWarning(exception, "Homepage forum replies-today count failed to load.");
            return null;
        }
    }

    /// <summary>
    /// "Happening now" lines: the newest item from each live feed, freshest first.
    /// Photos carry no reliable upload time, so they rotate in without one.
    /// </summary>
    private List<HomeTickerItem> BuildTicker()
    {
        var items = new List<(DateTime? At, HomeTickerItem Item)>();
        if (ForumThreads.Count > 0)
        {
            var thread = ForumThreads[0];
            items.Add((thread.LastActivityAt, new HomeTickerItem(
                $"Forum: {thread.Title}",
                thread.DetailPath,
                HomeRelativeTime.Format(thread.LastActivityAt, Now))));
        }

        if (Latest.Count > 0)
        {
            var news = Latest[0];
            items.Add((news.PublishedAt, new HomeTickerItem(
                $"News: {news.Title}",
                news.DetailPath,
                HomeRelativeTime.Format(news.PublishedAt, Now))));
        }

        if (LatestPhotos.Count > 0)
        {
            var photo = LatestPhotos[0];
            var caption = string.IsNullOrWhiteSpace(photo.Title) ? photo.CategoryName : $"{photo.CategoryName}, {photo.Title}";
            items.Add((null, new HomeTickerItem(
                $"New photo: {caption}",
                PhotoRoutes.GetDetailPath(photo.CategorySlug, photo.PicId),
                null)));
        }

        if (FeaturedArticles.Count > 0)
        {
            var article = FeaturedArticles[0];
            items.Add((null, new HomeTickerItem($"New article: {article.Title}", article.Href, null)));
        }

        return items
            .OrderByDescending(entry => entry.At ?? DateTime.MinValue)
            .Select(entry => entry.Item)
            .ToList();
    }

    private async Task LoadHomePollAsync(CancellationToken cancellationToken)
    {
        var memberAuth = await HttpContext.AuthenticateMemberAsync();
        var memberId = ForumMember.GetMemberId(memberAuth.Principal);
        HomePoll = await homePollRepository.GetCurrentAsync(memberId, cancellationToken);
        HomePollViewerCanVote = memberId is not null
            && HomePoll is { IsClosed: false, ViewerHasVoted: false };
    }
}

public sealed record HomeTickerItem(string Text, string Href, string? When);

public sealed record HomeArticleTeaser(
    string Image,
    string Category,
    string Title,
    string Excerpt,
    string Meta,
    string Href);
