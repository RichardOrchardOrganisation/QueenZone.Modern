using Microsoft.AspNetCore.Authentication;
using QueenZone.Data;

namespace QueenZone.Web;

public static class CrosswordPlayApiEndpoints
{
    internal static void MapCrosswordPlayApiEndpoints(this RouteGroupBuilder group)
    {
        Mutation(group.MapPost("/{id:guid}/check", CheckAsync), "CheckCrossword", QueenZoneRateLimitPolicies.AnonymousWrite)
            .Accepts<CrosswordCheckRequestDto>("application/json").Produces<CrosswordCheckResultDto>();
        Mutation(group.MapPost("/{id:guid}/reveal", RevealAsync), "RevealCrossword", QueenZoneRateLimitPolicies.AnonymousWrite)
            .Accepts<CrosswordRevealRequestDto>("application/json").Produces<CrosswordRevealResultDto>();
        group.MapGet("/{id:guid}/progress", GetProgressAsync).WithName("GetCrosswordProgress")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Produces<CrosswordProgressDto>().Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status404NotFound);
        Mutation(group.MapPut("/{id:guid}/progress", SaveProgressAsync), "SaveCrosswordProgress", QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Accepts<CrosswordProgressRequestDto>("application/json").Produces<CrosswordProgressDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        Mutation(group.MapPost("/{id:guid}/complete", CompleteAsync), "CompleteCrossword", QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Accepts<CrosswordProgressRequestDto>("application/json").Produces<CrosswordCompletionResultDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static RouteHandlerBuilder Mutation(RouteHandlerBuilder route, string name, string policy) =>
        route.WithName(name).RequireRateLimiting(policy)
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status429TooManyRequests);

    private static Task<IResult> CheckAsync(HttpContext context, Guid id, CrosswordCheckRequestDto request,
        ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress, CancellationToken cancellationToken) =>
        WithPuzzleAsync(context, id, catalog, async puzzle =>
        {
            ArgumentNullException.ThrowIfNull(request.Selection);
            var selection = request.Selection.ToSelection();
            var letters = CrosswordPlayRules.ExpandLetters(puzzle.Seed.Grid, request.Letters, selection);
            var cells = CrosswordPlayRules.Check(puzzle.Seed.Grid, letters, selection);
            if (request.AutoCheck && await BearerMemberAsync(context) is { } member)
            {
                await progress.MarkAssistanceAsync(id, member, [], true, cancellationToken);
            }
            return Results.Ok(new CrosswordCheckResultDto(cells,
                Explanations(puzzle.Seed.Grid, letters, []), CrosswordPlayRules.IsComplete(puzzle.Seed.Grid, letters)));
        }, cancellationToken);

    private static Task<IResult> RevealAsync(HttpContext context, Guid id, CrosswordRevealRequestDto request,
        ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress, CancellationToken cancellationToken) =>
        WithPuzzleAsync(context, id, catalog, async puzzle =>
        {
            ArgumentNullException.ThrowIfNull(request.Selection);
            var cells = CrosswordPlayRules.Reveal(puzzle.Seed.Grid, request.Selection.ToSelection());
            var indices = cells.Select(cell => cell.Index).ToArray();
            if (await BearerMemberAsync(context) is { } member)
            {
                await progress.MarkAssistanceAsync(id, member, indices, false, cancellationToken);
            }
            return Results.Ok(new CrosswordRevealResultDto(cells,
                Explanations(puzzle.Seed.Grid, CrosswordPlayRules.EmptyLetters(puzzle.Seed.Grid), indices), false));
        }, cancellationToken);

    private static Task<IResult> GetProgressAsync(HttpContext context, Guid id, ICrosswordCatalogRepository catalog,
        ICrosswordProgressRepository progress, CancellationToken cancellationToken) =>
        WithPuzzleAsync(context, id, catalog, async _ =>
        {
            var saved = await progress.GetAsync(id, Member(context), cancellationToken);
            return saved is null ? Results.NoContent() : Results.Ok(ToDto(saved));
        }, cancellationToken);

    private static Task<IResult> SaveProgressAsync(HttpContext context, Guid id, CrosswordProgressRequestDto request,
        ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress, CancellationToken cancellationToken) =>
        WithPuzzleAsync(context, id, catalog, async _ => Results.Ok(ToDto(await progress.SaveAsync(id,
            Member(context), request.ToWrite(), cancellationToken))), cancellationToken);

    private static Task<IResult> CompleteAsync(HttpContext context, Guid id, CrosswordProgressRequestDto request,
        ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress, CancellationToken cancellationToken) =>
        WithPuzzleAsync(context, id, catalog, async puzzle =>
        {
            var result = await progress.CompleteAsync(id, Member(context), request.ToWrite(), cancellationToken);
            var completion = result.Completion is { } completed ? new CrosswordCompletionDto(completed.ElapsedSeconds,
                completed.Clean, completed.RankingEligible, completed.CompletedAt) : null;
            IReadOnlyList<CrosswordAnswerReviewDto> review = result.Correct ? puzzle.Seed.Grid.Clues.Select(clue =>
                new CrosswordAnswerReviewDto(clue.Number, Direction(clue.Direction), clue.Answer, clue.Explanation)).ToArray() : [];
            return Results.Ok(new CrosswordCompletionResultDto(result.Correct, completion, review));
        }, cancellationToken);

    private static async Task<IResult> WithPuzzleAsync(HttpContext context, Guid id, ICrosswordCatalogRepository catalog,
        Func<CrosswordCatalogItem, Task<IResult>> action, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var puzzle = await catalog.GetByIdAsync(id, cancellationToken);
            var now = (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
            return puzzle is null || !CrosswordVisibility.IsPlayable(puzzle, now)
                ? ApiV1EndpointHelpers.NotFound("No playable crossword with that id.") : await action(puzzle);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid crossword input", detail: exception.Message);
        }
        catch (OptimisticConcurrencyException)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Crossword changed",
                detail: "Reload the crossword before continuing.");
        }
        catch (KeyNotFoundException)
        {
            return ApiV1EndpointHelpers.NotFound("No playable crossword with that id.");
        }
    }

    private static Guid Member(HttpContext context) => ForumMember.GetMemberId(context.User)
        ?? throw new InvalidOperationException("Member authorization requires a member id.");

    // Optional mutations accept bearer identity only. Website cookies use antiforgery-protected Razor handlers.
    private static async Task<Guid?> BearerMemberAsync(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            return null;
        }
        var authentication = await context.AuthenticateAsync(MemberAuthenticationSchemes.MembersBearer);
        return authentication.Succeeded ? ForumMember.GetMemberId(authentication.Principal) : null;
    }

    private static CrosswordProgressDto ToDto(CrosswordProgress progress) => new(progress.Letters,
        progress.ElapsedSeconds, progress.RevealedCells, progress.AutoCheckUsed, progress.UpdatedAt, progress.StartedAt);

    private static IReadOnlyList<CrosswordExplanationDto> Explanations(CrosswordGrid grid, string letters, IReadOnlyList<int> revealed) =>
        CrosswordPlayRules.GetExplanations(grid, letters, revealed).Select(item =>
            new CrosswordExplanationDto(item.Number, Direction(item.Direction), item.Explanation)).ToArray();

    private static string Direction(CrosswordDirection direction) => direction == CrosswordDirection.Across ? "across" : "down";
}
