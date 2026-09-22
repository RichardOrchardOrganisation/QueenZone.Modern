using Microsoft.AspNetCore.Authentication;
using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Public, read-only <c>/api/v1/content/*</c> routes for the mobile app
/// (issues #726 / #743 / #747 / #1100 / #1186). News, long-form articles,
/// biography, discography, timeline, Freddie Tribute, photo galleries,
/// fan-performance listings, and random trivia require no authentication:
/// that content is public on the website today.
/// Fan-performance audio at <c>/api/v1/content/fan-performances/{id}/audio</c>
/// requires <see cref="MemberAuthenticationSchemes.MobileMemberPolicy"/> and
/// reuses <see cref="FanPerformanceEndpoints.ServeAudioAsync"/> — the same
/// private <c>songfiles</c> blob stream as the website, including HTTP range
/// processing. Photo gallery pages reuse <see cref="IPhotoRepository"/>
/// and CDN URLs from <see cref="PhotoImageUrl"/>. Category list/detail/items
/// use <see cref="PublicQueryCacheService"/> (same helpers as Razor photography
/// pages); detail neighbors still come from <see cref="IPhotoRepository"/>.
/// Fan-performance list/detail use the same query cache so admin publish/hide
/// is visible without a process restart. News/articles archive pages, the
/// published timeline, random quote/trivia, biography chapters, and discography
/// albums also go through <see cref="PublicQueryCacheService"/> (repository
/// shapes only; mapping, paging, and random pick stay in the handlers).
/// Category items default and clamp <c>pageSize</c> to
/// <see cref="PhotoRoutes.CategoryPageSize"/>.
/// </summary>
public static class ContentApiEndpoints
{
    public const string RootPath = "/api/v1/content";

    public static void MapContentApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(RootPath)
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Content")
            .DisableAntiforgery();

        group.MapGet("/news", GetNewsListAsync)
            .WithName("GetContentNewsList")
            .WithSummary("Paged list of published news articles. Optional 'decade' (e.g. 2010) filters server-side to that 10-year span, or 'year' (e.g. 2008) to a single year; 'year' wins if both are given. Out-of-range years are ignored.")
            .Produces<ApiPagedResponse<NewsListItemDto>>();

        group.MapGet("/news/{id:int}", GetNewsDetailAsync)
            .WithName("GetContentNewsDetail")
            .WithSummary("A single published news article.")
            .Produces<NewsDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/news/years", GetNewsYearRangeAsync)
            .WithName("GetContentNewsYearRange")
            .WithSummary("Earliest/latest published years across the news archive, for the year-rail scrubber's tick marks.")
            .Produces<NewsYearRangeDto>();

        group.MapGet("/articles", GetArticlesListAsync)
            .WithName("GetContentArticlesList")
            .WithSummary("Paged list of published long-form archive articles. Editorial archive only — not news and not community submissions.")
            .Produces<ApiPagedResponse<ArticleListItemDto>>();

        group.MapGet("/articles/{id:int}", GetArticleDetailAsync)
            .WithName("GetContentArticleDetail")
            .WithSummary("A single published long-form archive article.")
            .Produces<ArticleDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/timeline", GetTimelineEventsAsync)
            .WithName("GetContentTimelineEvents")
            .WithSummary("Paged list of published history timeline events, in date order.")
            .Produces<ApiPagedResponse<TimelineEventDto>>();

        group.MapGet("/timeline/{id:int}", GetTimelineEventDetailAsync)
            .WithName("GetContentTimelineEventDetail")
            .WithSummary("A single published history timeline event by id. Unpublished or missing events return 404.")
            .Produces<TimelineEventDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/on-this-day", GetOnThisDayAsync)
            .WithName("GetContentOnThisDay")
            .WithSummary("The single most notable published history event for today's date, with a +/-7 day fallback when none. Matches the website home page.")
            .Produces<TimelineEventDto?>();

        group.MapGet("/live-activity", GetLiveActivityAsync)
            .WithName("GetContentLiveActivity")
            .WithSummary("Count of new forum replies posted today. No presence/reading tracking exists; this is the only honest live signal available.")
            .Produces<LiveActivitySummaryDto>();

        group.MapGet("/quotes/random", GetRandomQuoteAsync)
            .WithName("GetContentRandomQuote")
            .WithSummary("A single random published quote, matching the homepage widget. Intended for the mobile app's homescreen widget.")
            .Produces<QuoteDto?>();

        group.MapGet("/trivia/random", GetRandomTriviaAsync)
            .WithName("GetContentRandomTrivia")
            .WithSummary("A single random published trivia fact, matching the /trivia page. JSON null when none is published.")
            .Produces<TriviaDto?>();

        group.MapGet("/quotes/{id:int}", GetQuoteDetailAsync)
            .WithName("GetContentQuoteDetail")
            .WithSummary("A single published quote by id. Unpublished or missing quotes return 404.")
            .Produces<QuoteDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/home-poll", GetHomePollAsync)
            .WithName("GetContentHomePoll")
            .WithSummary("The current Home poll with public results. JSON null when none is live. Optional Bearer marks the viewer's choice.")
            .Produces<HomePollDto?>();

        group.MapPost("/home-poll/votes", VoteHomePollAsync)
            .WithName("VoteContentHomePoll")
            .WithSummary("Cast one ballot on the current Home poll. Votes are final.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .Accepts<HomePollVoteRequestDto>("application/json")
            .Produces<HomePollDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/quizzes", GetQuizzesAsync)
            .WithName("GetContentQuizzes")
            .WithSummary("Paged list of published quizzes. Unpublished quizzes never appear.")
            .Produces<ApiPagedResponse<QuizListItemDto>>();

        group.MapGet("/quizzes/leaderboard", GetQuizLeaderboardAsync)
            .WithName("GetContentQuizLeaderboard")
            .WithSummary("Quiz leaderboard ranked by summed attempt score. 'scope' is 'week' (default, current UTC week) or 'all'. Optional Bearer includes the viewer's own rank even outside the top page.")
            .Produces<QuizLeaderboardDto>();

        group.MapPost("/quizzes/sprint/start", StartSprintAsync)
            .WithName("StartContentQuizSprint")
            .WithSummary("Start a 60-second Quiz Sprint round: shuffled questions (no answer key) plus a signed ticket to send back on finish. Open to anonymous callers.")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AnonymousWrite)
            .Produces<SprintRoundDto>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/quizzes/sprint/answer", CheckSprintAnswer)
            .WithName("CheckContentQuizSprintAnswer")
            .WithSummary("Reveal whether one Sprint pick was right (and which option was) while the round is live, for per-answer feedback. The ticket keeps the answer key server-side.")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AnonymousWrite)
            .Accepts<SprintAnswerRequestDto>("application/json")
            .Produces<SprintAnswerResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/quizzes/sprint/finish", FinishSprintAsync)
            .WithName("FinishContentQuizSprint")
            .WithSummary("Score a Quiz Sprint round server-side (+1 per correct answer, +2 once on a streak of 3). A Bearer-authenticated run is recorded on today's leaderboard; anonymous runs are scored but not recorded.")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AnonymousWrite)
            .Accepts<SprintFinishRequestDto>("application/json")
            .Produces<SprintResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/quizzes/sprint/claim", ClaimSprintRunAsync)
            .WithName("ClaimContentQuizSprintRun")
            .WithSummary("Add a guest's finished Sprint run (via the claimToken from its finish response) to the signed-in member's leaderboard record. Valid for one hour; each token can be claimed once.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .Accepts<SprintClaimRequestDto>("application/json")
            .Produces<SprintClaimResultDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/quizzes/sprint/leaderboard", GetSprintBoardAsync)
            .WithName("GetContentQuizSprintBoard")
            .WithSummary("Quiz Sprint standings using each member's best run. 'scope' is 'daily' (default, today UTC), 'all' (best run ever) or 'total' (points summed over every run). Optional Bearer includes the viewer's own entry even outside the top page.")
            .Produces<SprintBoardDto>();

        group.MapGet("/quizzes/sprint/daily", GetSprintDailyBoardAsync)
            .WithName("GetContentQuizSprintDailyBoard")
            .WithSummary("Today's (UTC) Quiz Sprint standings using each member's best run, plus players today. Optional Bearer includes the viewer's own entry even outside the top page.")
            .Produces<SprintDailyBoardDto>();

        group.MapGet("/quizzes/{id:guid}", GetQuizDetailAsync)
            .WithName("GetContentQuizDetail")
            .WithSummary("A published quiz shaped for play: options only, no correct-answer flag.")
            .Produces<QuizDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/quizzes/{id:guid}/attempts", SubmitQuizAsync)
            .WithName("SubmitContentQuizAttempt")
            .WithSummary("Score answers server-side against the quiz's stored correct options and record the attempt. Correct answers are only ever revealed in this response.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .Accepts<QuizSubmitRequestDto>("application/json")
            .Produces<QuizResultDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapGet("/biography", GetBiographyChaptersAsync)
            .WithName("GetContentBiographyChapters")
            .WithSummary("Paged list of biography chapters, in reading order.")
            .Produces<ApiPagedResponse<BiographyChapterListItemDto>>();

        group.MapGet("/biography/{id:int}", GetBiographyChapterDetailAsync)
            .WithName("GetContentBiographyChapterDetail")
            .WithSummary("A single biography chapter, with adjacent-chapter navigation.")
            .Produces<BiographyChapterDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/discography", GetAlbumsAsync)
            .WithName("GetContentDiscographyAlbums")
            .WithSummary("Paged list of studio albums.")
            .Produces<ApiPagedResponse<AlbumListItemDto>>();

        group.MapGet("/discography/{id:int}", GetAlbumDetailAsync)
            .WithName("GetContentDiscographyAlbumDetail")
            .WithSummary("A single studio album, with its track list.")
            .Produces<AlbumDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/freddietribute", GetFreddieTributesAsync)
            .WithName("GetContentFreddieTributes")
            .WithSummary("Paged list of Freddie Mercury tributes.")
            .Produces<ApiPagedResponse<FreddieTributeDto>>();

        group.MapGet("/photos/categories", GetPhotoCategoriesAsync)
            .WithName("GetContentPhotoCategories")
            .WithSummary("Paged list of public photo gallery categories.")
            .Produces<ApiPagedResponse<PhotoCategoryListItemDto>>();

        group.MapGet("/photos/categories/{slug}", GetPhotoCategoryAsync)
            .WithName("GetContentPhotoCategory")
            .WithSummary("A single public photo gallery category.")
            .Produces<PhotoCategoryListItemDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/photos/categories/{slug}/items", GetPhotoCategoryItemsAsync)
            .WithName("GetContentPhotoCategoryItems")
            .WithSummary("Paged photos in a gallery. pageSize defaults and clamps to 24, matching /photography/{slug}.")
            .Produces<ApiPagedResponse<PhotoListItemDto>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/photos/categories/{slug}/items/{picId:int}", GetPhotoDetailAsync)
            .WithName("GetContentPhotoDetail")
            .WithSummary("A single public photo, with prev/next neighbors matching the website lightbox.")
            .Produces<PhotoDetailDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/fan-performances", GetFanPerformancesAsync)
            .WithName("GetContentFanPerformances")
            .WithSummary("Paged list of public fan-stage recordings. Duration is MPEG metadata when available.")
            .Produces<ApiPagedResponse<FanPerformanceDto>>();

        group.MapGet("/fan-performances/{id:int}", GetFanPerformanceDetailAsync)
            .WithName("GetContentFanPerformanceDetail")
            .WithSummary("A single public fan-stage recording, including duration and the member-gated audio path.")
            .Produces<FanPerformanceDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/fan-performances/{id:int}/audio", GetFanPerformanceAudioAsync)
            .WithName("GetContentFanPerformanceAudio")
            .WithSummary("Member-gated audio stream. Same blob and range support as /fan-performances/{id}/audio.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(FanPerformanceRateLimitingOptions.AudioPolicy)
            .Produces(StatusCodes.Status200OK, contentType: "audio/mpeg")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> GetNewsListAsync(
        PublicQueryCacheService publicQueryCache,
        NewsDiscussionComposer newsDiscussion,
        int? page,
        int? pageSize,
        int? decade,
        int? year,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var filter = NewsArchiveFilter.Parse(decade, year);
        var items = await publicQueryCache.GetNewsArchivePageAsync(
            request.Page,
            request.PageSize,
            filter,
            cancellationToken);
        var totalCount = await publicQueryCache.GetNewsPublishedCountAsync(filter, cancellationToken);

        var response = ApiPagedResponse<NewsListItemDto>.Create(
            await newsDiscussion.ToListItemsAsync(items, cancellationToken),
            request.Page,
            request.PageSize,
            totalCount);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetNewsYearRangeAsync(
        INewsRepository newsRepository,
        CancellationToken cancellationToken)
    {
        var range = await newsRepository.GetArchiveYearRangeAsync(cancellationToken);
        return Results.Ok(new NewsYearRangeDto(range.MinYear, range.MaxYear));
    }

    internal static async Task<IResult> GetNewsDetailAsync(
        INewsRepository newsRepository,
        NewsDiscussionComposer newsDiscussion,
        int id,
        CancellationToken cancellationToken)
    {
        var item = await newsRepository.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No published news article with id '{id}'.");
        }

        return Results.Ok(await newsDiscussion.ToDetailAsync(item, cancellationToken));
    }

    internal static async Task<IResult> GetArticlesListAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize, ArticlesRoutes.ArchivePageSize);
        var items = await publicQueryCache.GetArticlesArchivePageAsync(
            request.Page,
            request.PageSize,
            cancellationToken);
        var totalCount = await publicQueryCache.GetArticlePublishedCountAsync(cancellationToken);

        var response = ApiPagedResponse<ArticleListItemDto>.Create(
            ContentApiMapper.ToArticleListItems(items),
            request.Page,
            request.PageSize,
            totalCount);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetArticleDetailAsync(
        IArticlesRepository articlesRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var item = await articlesRepository.GetByIdAsync(id, cancellationToken);
        if (item is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No published article with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToArticleDetail(item));
    }

    internal static async Task<IResult> GetTimelineEventsAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var events = (await publicQueryCache.GetAllPublishedHistoryEventsAsync(cancellationToken))
            .OrderBy(e => e.EventDate)
            .ThenByDescending(e => e.Importance)
            .ToList();

        var pageItems = events
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var response = ApiPagedResponse<TimelineEventDto>.Create(
            ContentApiMapper.ToTimelineEvents(pageItems),
            request.Page,
            request.PageSize,
            events.Count);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetTimelineEventDetailAsync(
        PublicQueryCacheService publicQueryCache,
        int id,
        CancellationToken cancellationToken)
    {
        var historyEvent = (await publicQueryCache.GetAllPublishedHistoryEventsAsync(cancellationToken))
            .FirstOrDefault(item => item.Id == id);
        if (historyEvent is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No published timeline event with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToTimelineEvent(historyEvent));
    }

    internal static async Task<IResult> GetOnThisDayAsync(
        PublicQueryCacheService publicQueryCache,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var events = await publicQueryCache.GetOnThisDayAsync(today, 1, cancellationToken);
        if (events.Count == 0)
        {
            events = await publicQueryCache.GetAroundThisDayAsync(today, 7, 1, cancellationToken);
        }

        // ASP.NET Core Ok(null) / Json(null) write an empty 200. The contract is JSON null.
        TimelineEventDto? payload = events.Count > 0 ? ContentApiMapper.ToTimelineEvent(events[0]) : null;
        return payload is null
            ? Results.Content("null", "application/json")
            : Results.Ok(payload);
    }

    internal static async Task<IResult> GetLiveActivityAsync(
        PublicQueryCacheService publicQueryCache,
        CancellationToken cancellationToken)
    {
        var count = await publicQueryCache.GetLiveActivityNewForumRepliesTodayAsync(cancellationToken);
        return Results.Ok(new LiveActivitySummaryDto(count));
    }

    internal static async Task<IResult> GetRandomQuoteAsync(
        PublicQueryCacheService publicQueryCache,
        CancellationToken cancellationToken)
    {
        var quote = await publicQueryCache.GetRandomPublishedQuoteAsync(cancellationToken);

        // ASP.NET Core Ok(null) / Json(null) write an empty 200. The contract is JSON null.
        QuoteDto? payload = quote is null ? null : ContentApiMapper.ToQuoteDto(quote);
        return payload is null
            ? Results.Content("null", "application/json")
            : Results.Ok(payload);
    }

    internal static async Task<IResult> GetRandomTriviaAsync(
        PublicQueryCacheService publicQueryCache,
        CancellationToken cancellationToken)
    {
        var fact = await publicQueryCache.GetRandomPublishedTriviaAsync(cancellationToken);

        // ASP.NET Core Ok(null) / Json(null) write an empty 200. The contract is JSON null.
        TriviaDto? payload = fact is null ? null : ContentApiMapper.ToTriviaDto(fact);
        return payload is null
            ? Results.Content("null", "application/json")
            : Results.Ok(payload);
    }

    internal static async Task<IResult> GetHomePollAsync(
        HttpContext httpContext,
        IHomePollRepository homePollRepository,
        CancellationToken cancellationToken)
    {
        var poll = await homePollRepository.GetCurrentAsync(
            await TryGetViewerMemberIdAsync(httpContext),
            cancellationToken);

        // ASP.NET Core Ok(null) / Json(null) write an empty 200. The contract is JSON null.
        HomePollDto? payload = poll is null ? null : ContentApiMapper.ToHomePollDto(poll);
        return payload is null
            ? Results.Content("null", "application/json")
            : Results.Ok(payload);
    }

    internal static async Task<IResult> VoteHomePollAsync(
        HttpContext httpContext,
        HomePollVoteRequestDto? request,
        HomePollVoteService voteService,
        IHomePollRepository homePollRepository,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(httpContext.User);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        if (request?.OptionId is not Guid optionId || optionId == Guid.Empty)
        {
            return ForumPollVoteMapper.ToProblemResult(
                new ForumPollVoteException(
                    ForumPollVoteException.InvalidOptions,
                    "Select an option."));
        }

        try
        {
            await voteService.CastVoteAsync(memberId.Value, optionId, cancellationToken);
        }
        catch (ForumPollVoteException ex)
        {
            return ForumPollVoteMapper.ToProblemResult(ex);
        }

        var poll = await homePollRepository.GetCurrentAsync(memberId, cancellationToken);
        return poll is null
            ? Results.Content("null", "application/json")
            : Results.Ok(ContentApiMapper.ToHomePollDto(poll));
    }

    internal static async Task<IResult> GetQuizzesAsync(
        IQuizRepository quizRepository,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var all = await quizRepository.GetPublishedAsync(cancellationToken);
        var items = all
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(ContentApiMapper.ToQuizListItemDto)
            .ToList();

        var response = ApiPagedResponse<QuizListItemDto>.Create(items, request.Page, request.PageSize, all.Count);
        return Results.Ok(response);
    }

    internal static async Task<IResult> GetQuizDetailAsync(
        IQuizRepository quizRepository,
        Guid id,
        CancellationToken cancellationToken)
    {
        var quiz = await quizRepository.GetPublishedForPlayAsync(id, cancellationToken);
        if (quiz is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No published quiz with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToQuizDetailDto(quiz));
    }

    internal static async Task<IResult> SubmitQuizAsync(
        HttpContext httpContext,
        Guid id,
        QuizSubmitRequestDto? request,
        IQuizRepository quizRepository,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(httpContext.User);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var answers = (request?.Answers ?? [])
            .Select(answer => new QuizAnswerSubmission(answer.QuestionId, answer.SelectedOptionId))
            .ToList();

        var result = await quizRepository.SubmitAsync(id, memberId, answers, cancellationToken);
        if (result is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No published quiz with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToQuizResultDto(result));
    }

    internal static async Task<IResult> GetQuizLeaderboardAsync(
        HttpContext httpContext,
        string? scope,
        IQuizRepository quizRepository,
        IMemberAccountRepository memberAccountRepository,
        CancellationToken cancellationToken)
    {
        var leaderboardScope = string.Equals(scope, "all", StringComparison.OrdinalIgnoreCase)
            ? QuizLeaderboardScope.AllTime
            : QuizLeaderboardScope.Week;
        var viewerId = await TryGetViewerMemberIdAsync(httpContext);

        var result = await quizRepository.GetLeaderboardAsync(leaderboardScope, viewerId, top: 20, cancellationToken);
        var top = await ToLeaderboardEntryDtosAsync(result.Top, memberAccountRepository, cancellationToken);
        var viewer = result.Viewer is null
            ? null
            : (await ToLeaderboardEntryDtosAsync([result.Viewer], memberAccountRepository, cancellationToken)).Single();

        return Results.Ok(new QuizLeaderboardDto(top, viewer, result.TotalMembers));
    }

    internal static async Task<IResult> StartSprintAsync(
        HttpContext httpContext,
        QuizSprintService sprintService,
        CancellationToken cancellationToken)
    {
        var round = await sprintService.StartAsync(cancellationToken, QuizSprintSeenQuestions.Read(httpContext.Request));
        if (round is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: "No published quiz questions are available yet.");
        }

        return Results.Ok(new SprintRoundDto(
            round.Ticket,
            round.ServerNowUnixMilliseconds,
            round.ExpiresAtUnixMilliseconds,
            QuizSprintService.DurationSeconds,
            round.Questions
                .Select(question => new SprintQuestionDto(
                    question.Id,
                    question.Text,
                    question.Options.Select(option => new SprintOptionDto(option.Id, option.Text)).ToList()))
                .ToList()));
    }

    internal static IResult CheckSprintAnswer(
        SprintAnswerRequestDto? request,
        QuizSprintService sprintService)
    {
        var check = request is null
            ? null
            : sprintService.CheckAnswer(request.Ticket, request.QuestionId, request.OptionId);
        return check is null
            ? Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "The sprint ticket, question or option is invalid, or the round has ended.")
            : Results.Ok(new SprintAnswerResultDto(check.IsCorrect, check.CorrectOptionId));
    }

    internal static async Task<IResult> FinishSprintAsync(
        HttpContext httpContext,
        SprintFinishRequestDto? request,
        QuizSprintService sprintService,
        CancellationToken cancellationToken)
    {
        var selections = new Dictionary<Guid, Guid>();
        foreach (var answer in request?.Answers ?? [])
        {
            if (answer.SelectedOptionId is Guid optionId)
            {
                selections[answer.QuestionId] = optionId;
            }
        }

        var memberId = await TryGetViewerMemberIdAsync(httpContext);
        var outcome = await sprintService.FinishAsync(request?.Ticket, selections, memberId, cancellationToken);
        switch (outcome.Status)
        {
            case SprintFinishStatus.Invalid:
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "The sprint ticket is missing or invalid.");
            case SprintFinishStatus.Expired:
                return Results.Problem(
                    statusCode: StatusCodes.Status410Gone,
                    title: "Round expired",
                    detail: "Answers arrived after the 60-second round ended.");
            default:
                QuizSprintSeenQuestions.Remember(httpContext, outcome.AnsweredQuestionIds ?? []);
                var result = outcome.Result!;
                return Results.Ok(new SprintResultDto(
                    result.Attempted,
                    result.Correct,
                    result.Points,
                    result.BestStreak,
                    result.Recorded,
                    result.Rank,
                    result.Answers
                        .Select(item => new SprintReviewItemDto(item.QuestionId, item.QuestionText, item.IsCorrect, item.CorrectAnswer))
                        .ToList(),
                    result.ClaimToken));
        }
    }

    internal static async Task<IResult> ClaimSprintRunAsync(
        HttpContext httpContext,
        SprintClaimRequestDto? request,
        QuizSprintService sprintService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(httpContext.User);
        if (memberId is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");
        }

        var outcome = await sprintService.ClaimAsync(request?.ClaimToken, memberId.Value, cancellationToken);
        return outcome.Status switch
        {
            SprintClaimStatus.Claimed => Results.Ok(new SprintClaimResultDto("claimed", outcome.Points, outcome.Rank)),
            SprintClaimStatus.AlreadyClaimed => Results.Ok(new SprintClaimResultDto("already_claimed", outcome.Points, null)),
            SprintClaimStatus.Expired => Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "Claim expired",
                detail: "That run finished too long ago to add to the leaderboard."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "The claim token is missing or invalid."),
        };
    }

    internal static async Task<IResult> GetSprintBoardAsync(
        HttpContext httpContext,
        string? scope,
        IQuizRepository quizRepository,
        IMemberAccountRepository memberAccountRepository,
        CancellationToken cancellationToken)
    {
        var (boardScope, scopeName) = scope?.ToLowerInvariant() switch
        {
            "all" => (QuizSprintBoardScope.AllTime, "all"),
            "total" => (QuizSprintBoardScope.Total, "total"),
            _ => (QuizSprintBoardScope.Daily, "daily"),
        };
        var viewerId = await TryGetViewerMemberIdAsync(httpContext);
        var board = await quizRepository.GetSprintBoardAsync(
            boardScope,
            viewerId,
            top: 20,
            cancellationToken);
        var dto = await ToSprintBoardEntriesAsync(board, memberAccountRepository, cancellationToken);
        return Results.Ok(new SprintBoardDto(scopeName, dto.Top, dto.Viewer, board.Players));
    }

    internal static async Task<IResult> GetSprintDailyBoardAsync(
        HttpContext httpContext,
        IQuizRepository quizRepository,
        IMemberAccountRepository memberAccountRepository,
        CancellationToken cancellationToken)
    {
        var viewerId = await TryGetViewerMemberIdAsync(httpContext);
        var board = await quizRepository.GetSprintBoardAsync(QuizSprintBoardScope.Daily, viewerId, top: 20, cancellationToken);
        var dto = await ToSprintBoardEntriesAsync(board, memberAccountRepository, cancellationToken);
        return Results.Ok(new SprintDailyBoardDto(dto.Top, dto.Viewer, board.Players));
    }

    private static async Task<(IReadOnlyList<SprintLeaderboardEntryDto> Top, SprintLeaderboardEntryDto? Viewer)> ToSprintBoardEntriesAsync(
        QuizSprintBoardResult board,
        IMemberAccountRepository memberAccountRepository,
        CancellationToken cancellationToken)
    {
        async Task<SprintLeaderboardEntryDto> ToDtoAsync(QuizSprintLeaderboardEntry entry)
        {
            var account = await memberAccountRepository.FindByIdAsync(entry.MemberAccountId, cancellationToken);
            return new SprintLeaderboardEntryDto(entry.Rank, account?.DisplayName ?? "Member", entry.Score, entry.BestStreak, entry.Runs);
        }

        var top = new List<SprintLeaderboardEntryDto>(board.Top.Count);
        foreach (var entry in board.Top)
        {
            top.Add(await ToDtoAsync(entry));
        }

        var viewer = board.Viewer is null ? null : await ToDtoAsync(board.Viewer);
        return (top, viewer);
    }

    private static async Task<IReadOnlyList<QuizLeaderboardEntryDto>> ToLeaderboardEntryDtosAsync(
        IReadOnlyList<QuizLeaderboardEntry> entries,
        IMemberAccountRepository memberAccountRepository,
        CancellationToken cancellationToken)
    {
        var dtos = new List<QuizLeaderboardEntryDto>(entries.Count);
        foreach (var entry in entries)
        {
            var account = await memberAccountRepository.FindByIdAsync(entry.MemberAccountId, cancellationToken);
            dtos.Add(new QuizLeaderboardEntryDto(entry.Rank, account?.DisplayName ?? "Member", entry.Score, entry.AttemptCount));
        }

        return dtos;
    }

    internal static async Task<IResult> GetQuoteDetailAsync(
        IQuoteRepository quoteRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var quote = await quoteRepository.GetByIdAsync(id, cancellationToken);
        if (quote is null || !quote.IsPublished)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No published quote with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToQuoteDto(quote));
    }

    internal static async Task<IResult> GetBiographyChaptersAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var chapters = BiographyChapterOrdering.ByDisplaySequenceAscending(
            await publicQueryCache.GetBiographyChaptersAsync(cancellationToken));

        var pageItems = chapters
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var response = ApiPagedResponse<BiographyChapterListItemDto>.Create(
            ContentApiMapper.ToBiographyChapterListItems(pageItems),
            request.Page,
            request.PageSize,
            chapters.Count);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetBiographyChapterDetailAsync(
        IBiographyRepository biographyRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var chapter = await biographyRepository.GetByIdAsync(id, cancellationToken);
        if (chapter is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No biography chapter with id '{id}'.");
        }

        var navigation = await biographyRepository.GetAdjacentChaptersAsync(id, cancellationToken);
        return Results.Ok(ContentApiMapper.ToBiographyChapterDetail(chapter, navigation));
    }

    internal static async Task<IResult> GetAlbumsAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var albums = await publicQueryCache.GetDiscographyAlbumsAsync(cancellationToken);

        var pageItems = albums
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var response = ApiPagedResponse<AlbumListItemDto>.Create(
            ContentApiMapper.ToAlbumListItems(pageItems),
            request.Page,
            request.PageSize,
            albums.Count);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetAlbumDetailAsync(
        IDiscographyRepository discographyRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var album = await discographyRepository.GetAlbumByIdAsync(id, cancellationToken);
        if (album is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No album with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToAlbumDetail(album));
    }

    internal static async Task<IResult> GetFreddieTributesAsync(
        IFreddieTributeRepository tributeRepository,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var tributePage = await tributeRepository.GetPageAsync(request.Page, request.PageSize, cancellationToken);

        var response = ApiPagedResponse<FreddieTributeDto>.Create(
            ContentApiMapper.ToFreddieTributeDtos(tributePage.Items),
            request.Page,
            request.PageSize,
            tributePage.TotalCount);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetPhotoCategoriesAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var categories = await publicQueryCache.GetPhotoCategoriesAsync(cancellationToken);

        var pageItems = categories
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        var response = ApiPagedResponse<PhotoCategoryListItemDto>.Create(
            ContentApiMapper.ToPhotoCategoryListItems(pageItems),
            request.Page,
            request.PageSize,
            categories.Count);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetPhotoCategoryAsync(
        PublicQueryCacheService publicQueryCache,
        string slug,
        CancellationToken cancellationToken)
    {
        var category = await publicQueryCache.GetPhotoCategoryBySlugAsync(slug, cancellationToken);
        if (category is null)
        {
            return PhotoCategoryNotFound(slug);
        }

        return Results.Ok(ContentApiMapper.ToPhotoCategoryListItem(category));
    }

    internal static async Task<IResult> GetPhotoCategoryItemsAsync(
        PublicQueryCacheService publicQueryCache,
        string slug,
        int? page,
        int? pageSize,
        string? size,
        CancellationToken cancellationToken)
    {
        var category = await publicQueryCache.GetPhotoCategoryBySlugAsync(slug, cancellationToken);
        if (category is null)
        {
            return PhotoCategoryNotFound(slug);
        }

        var request = ApiPagination.Normalize(
            page,
            pageSize,
            PhotoRoutes.CategoryPageSize,
            PhotoRoutes.CategoryPageSize);
        var filter = PhotoListFilter.Parse(size);
        var result = await publicQueryCache.GetPhotoCategoryPageAsync(
            category.CatId,
            request.Page,
            request.PageSize,
            filter,
            cancellationToken);

        var response = ApiPagedResponse<PhotoListItemDto>.Create(
            ContentApiMapper.ToPhotoListItems(result.Items, filter),
            request.Page,
            request.PageSize,
            result.TotalCount);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetPhotoDetailAsync(
        PublicQueryCacheService publicQueryCache,
        IPhotoRepository photoRepository,
        string slug,
        int picId,
        string? size,
        CancellationToken cancellationToken)
    {
        var category = await publicQueryCache.GetPhotoCategoryBySlugAsync(slug, cancellationToken);
        if (category is null)
        {
            return PhotoCategoryNotFound(slug);
        }

        var filter = PhotoListFilter.Parse(size);
        var navigation = await photoRepository.GetDetailNavigationAsync(
            category.CatId,
            picId,
            filter,
            cancellationToken);
        if (navigation is null)
        {
            // Active filter that excludes this photo: fall back to unfiltered navigation
            // so deep links work, matching Photography/Detail.cshtml.cs.
            if (filter.IsActive)
            {
                navigation = await photoRepository.GetDetailNavigationAsync(
                    category.CatId,
                    picId,
                    PhotoListFilter.None,
                    cancellationToken);
                if (navigation is null)
                {
                    return PhotoNotFound(slug, picId);
                }

                filter = PhotoListFilter.None;
            }
            else
            {
                return PhotoNotFound(slug, picId);
            }
        }

        return Results.Ok(ContentApiMapper.ToPhotoDetail(category, navigation, filter));
    }

    internal static async Task<IResult> GetFanPerformancesAsync(
        PublicQueryCacheService publicQueryCache,
        FanPerformanceDurationResolver durationResolver,
        FanPerformanceCreditResolver creditResolver,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var items = await creditResolver.EnrichAsync(
            await publicQueryCache.GetFanPerformancePageAsync(request.Page, request.PageSize, cancellationToken),
            cancellationToken);
        var totalCount = await publicQueryCache.GetFanPerformanceVisibleCountAsync(cancellationToken);
        var durations = await durationResolver.ResolveManyAsync(items, cancellationToken);

        var response = ApiPagedResponse<FanPerformanceDto>.Create(
            ContentApiMapper.ToFanPerformanceDtos(items, durations),
            request.Page,
            request.PageSize,
            totalCount);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetFanPerformanceDetailAsync(
        PublicQueryCacheService publicQueryCache,
        FanPerformanceDurationResolver durationResolver,
        FanPerformanceCreditResolver creditResolver,
        int id,
        CancellationToken cancellationToken)
    {
        var performance = await creditResolver.EnrichOneAsync(
            await publicQueryCache.GetFanPerformanceByIdAsync(id, cancellationToken),
            cancellationToken);
        if (performance is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No public fan performance with id '{id}'.");
        }

        var duration = await durationResolver.ResolveAsync(performance, cancellationToken);
        return Results.Ok(ContentApiMapper.ToFanPerformanceDto(performance, duration));
    }

    internal static Task<IResult> GetFanPerformanceAudioAsync(
        int id,
        IFanPerformanceRepository fanPerformanceRepository,
        IBlobUploadService blobUploadService,
        CancellationToken cancellationToken) =>
        FanPerformanceEndpoints.ServeAudioAsync(
            id,
            fanPerformanceRepository,
            blobUploadService,
            cancellationToken);

    private static async Task<Guid?> TryGetViewerMemberIdAsync(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.ContainsKey("Authorization"))
        {
            var bearer = await httpContext.AuthenticateAsync(MemberAuthenticationSchemes.MembersBearer);
            if (bearer.Succeeded)
            {
                return ForumMember.GetMemberId(bearer.Principal);
            }
        }

        var member = await httpContext.AuthenticateMemberAsync();
        return member.Succeeded ? ForumMember.GetMemberId(member.Principal) : null;
    }

    private static IResult PhotoCategoryNotFound(string slug) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: $"No public photo category with slug '{slug}'.");

    private static IResult PhotoNotFound(string slug, int picId) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: $"No public photo '{picId}' in category '{slug}'.");
}
