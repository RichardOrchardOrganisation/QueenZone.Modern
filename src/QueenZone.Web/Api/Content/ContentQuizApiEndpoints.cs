using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Published quiz, attempt, leaderboard, and Quiz Sprint routes.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentQuizApiEndpoints
{
    internal static void MapContentQuizApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapPagedList<QuizListItemDto>(
            "/quizzes",
            GetQuizzesAsync,
            "GetContentQuizzes",
            "Paged list of published quizzes. Unpublished quizzes never appear.");

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

        group.MapDetail<QuizDetailDto>(
            "/quizzes/{id:guid}",
            GetQuizDetailAsync,
            "GetContentQuizDetail",
            "A published quiz shaped for play: options only, no correct-answer flag.");

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
    }

    internal static async Task<IResult> GetQuizzesAsync(
        IQuizRepository quizRepository,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var all = await quizRepository.GetPublishedAsync(cancellationToken);
        return ApiV1EndpointHelpers.OkPagedSlice(
            all,
            page,
            pageSize,
            static items => items.Select(ContentApiMapper.ToQuizListItemDto).ToList());
    }

    internal static async Task<IResult> GetQuizDetailAsync(
        IQuizRepository quizRepository,
        Guid id,
        CancellationToken cancellationToken)
    {
        var quiz = await quizRepository.GetPublishedForPlayAsync(id, cancellationToken);
        if (quiz is null)
        {
            return ApiV1EndpointHelpers.NotFound($"No published quiz with id '{id}'.");
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
        var viewerId = await ContentApiEndpoints.TryGetViewerMemberIdAsync(httpContext);

        var result = await quizRepository.GetLeaderboardAsync(leaderboardScope, viewerId, top: 20, cancellationToken);
        var names = await QuizLeaderboardNameReader.LoadAsync(memberAccountRepository,
            result.Top.Select(entry => entry.MemberAccountId), result.Viewer?.MemberAccountId, cancellationToken);
        var top = ToLeaderboardEntryDtos(result.Top, names);
        var viewer = result.Viewer is null
            ? null
            : ToLeaderboardEntryDtos([result.Viewer], names).Single();

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

        var memberId = await ContentApiEndpoints.TryGetViewerMemberIdAsync(httpContext);
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
        var viewerId = await ContentApiEndpoints.TryGetViewerMemberIdAsync(httpContext);
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
        var viewerId = await ContentApiEndpoints.TryGetViewerMemberIdAsync(httpContext);
        var board = await quizRepository.GetSprintBoardAsync(QuizSprintBoardScope.Daily, viewerId, top: 20, cancellationToken);
        var dto = await ToSprintBoardEntriesAsync(board, memberAccountRepository, cancellationToken);
        return Results.Ok(new SprintDailyBoardDto(dto.Top, dto.Viewer, board.Players));
    }

    private static async Task<(IReadOnlyList<SprintLeaderboardEntryDto> Top, SprintLeaderboardEntryDto? Viewer)> ToSprintBoardEntriesAsync(
        QuizSprintBoardResult board,
        IMemberAccountRepository memberAccountRepository,
        CancellationToken cancellationToken)
    {
        var names = await QuizLeaderboardNameReader.LoadAsync(memberAccountRepository,
            board.Top.Select(entry => entry.MemberAccountId), board.Viewer?.MemberAccountId, cancellationToken);

        SprintLeaderboardEntryDto ToDto(QuizSprintLeaderboardEntry entry)
        {
            return new SprintLeaderboardEntryDto(entry.Rank,
                QuizLeaderboardNameReader.DisplayName(names, entry.MemberAccountId),
                entry.Score, entry.BestStreak, entry.Runs);
        }

        var top = board.Top.Select(ToDto).ToList();
        var viewer = board.Viewer is null ? null : ToDto(board.Viewer);
        return (top, viewer);
    }

    private static IReadOnlyList<QuizLeaderboardEntryDto> ToLeaderboardEntryDtos(
        IReadOnlyList<QuizLeaderboardEntry> entries,
        IReadOnlyDictionary<Guid, string> names)
    {
        return entries.Select(entry => new QuizLeaderboardEntryDto(entry.Rank,
            QuizLeaderboardNameReader.DisplayName(names, entry.MemberAccountId),
            entry.Score, entry.AttemptCount)).ToList();
    }
}
