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
            return Results.Ok(await CrosswordPlayActions.CheckAsync(puzzle, request, await BearerMemberAsync(context), progress, cancellationToken));
        }, cancellationToken);

    private static Task<IResult> RevealAsync(HttpContext context, Guid id, CrosswordRevealRequestDto request,
        ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress, CancellationToken cancellationToken) =>
        WithPuzzleAsync(context, id, catalog, async puzzle =>
        {
            return Results.Ok(await CrosswordPlayActions.RevealAsync(puzzle, request, await BearerMemberAsync(context), progress, cancellationToken));
        }, cancellationToken);

    private static Task<IResult> GetProgressAsync(HttpContext context, Guid id, ICrosswordCatalogRepository catalog,
        ICrosswordProgressRepository progress, CancellationToken cancellationToken) =>
        WithPuzzleAsync(context, id, catalog, async _ =>
        {
            var saved = await progress.GetAsync(id, Member(context), cancellationToken);
            return saved is null ? Results.NoContent() : Results.Ok(CrosswordPlayActions.Progress(saved));
        }, cancellationToken);

    private static Task<IResult> SaveProgressAsync(HttpContext context, Guid id, CrosswordProgressRequestDto request,
        ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress, CancellationToken cancellationToken) =>
        WithPuzzleAsync(context, id, catalog, async _ => Results.Ok(CrosswordPlayActions.Progress(await progress.SaveAsync(id,
            Member(context), request.ToWrite(), cancellationToken))), cancellationToken);

    private static Task<IResult> CompleteAsync(HttpContext context, Guid id, CrosswordProgressRequestDto request,
        ICrosswordCatalogRepository catalog, ICrosswordProgressRepository progress, CancellationToken cancellationToken) =>
        WithPuzzleAsync(context, id, catalog, async puzzle =>
        {
            return Results.Ok(await CrosswordPlayActions.CompleteAsync(puzzle, request, Member(context), progress, cancellationToken));
        }, cancellationToken);

    private static async Task<IResult> WithPuzzleAsync(HttpContext context, Guid id, ICrosswordCatalogRepository catalog,
        Func<CrosswordCatalogItem, Task<IResult>> action, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        try
        {
            var puzzle = await catalog.GetByIdAsync(id, cancellationToken);
            var now = (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
            if (puzzle is null || !CrosswordVisibility.IsPlayable(puzzle, now))
            {
                return ApiV1EndpointHelpers.NotFound("No playable crossword with that id.");
            }
            var result = await action(puzzle);
            var latest = await catalog.GetByIdAsync(id, cancellationToken);
            if (latest is null || !CrosswordVisibility.IsPlayable(latest,
                    (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow()))
            {
                return ApiV1EndpointHelpers.NotFound("No playable crossword with that id.");
            }
            if (latest.PlayVersion != puzzle.PlayVersion)
            {
                throw new OptimisticConcurrencyException("Crossword changed while processing the request.");
            }
            return result;
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

}
