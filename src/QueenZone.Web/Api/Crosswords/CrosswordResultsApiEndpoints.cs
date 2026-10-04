using QueenZone.Data;

namespace QueenZone.Web;

public static class CrosswordResultsApiEndpoints
{
    internal static void MapCrosswordResultsApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}/leaderboard", LeaderboardAsync).WithName("GetCrosswordLeaderboard")
            .WithSummary("Fastest 50 eligible clean first solves. Optional member authentication includes your rank outside the top 50.")
            .Produces<CrosswordLeaderboardDto>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/mine", HistoryAsync).WithName("GetMyCrosswords")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Produces<CrosswordHistoryDto>().ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> LeaderboardAsync(HttpContext context, Guid id, ICrosswordCatalogRepository catalog,
        ICrosswordProgressRepository progress, IMemberAccountRepository members, TimeProvider clock, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var puzzle = await catalog.GetByIdAsync(id, cancellationToken);
        if (puzzle is null || !CrosswordVisibility.IsPlayable(puzzle, clock.GetUtcNow())) return ApiV1EndpointHelpers.NotFound("No playable crossword with that id.");
        var viewer = await ContentApiEndpoints.TryGetViewerMemberIdAsync(context);
        return Results.Ok(await CrosswordResultsReader.LeaderboardAsync(id, viewer, progress, members, cancellationToken));
    }

    private static async Task<IResult> HistoryAsync(HttpContext context, ICrosswordCatalogRepository catalog,
        ICrosswordProgressRepository progress, TimeProvider clock, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        var member = ForumMember.GetMemberId(context.User);
        if (member is null) return Results.Unauthorized();
        return Results.Ok(await CrosswordResultsReader.HistoryAsync(member.Value, catalog, progress, clock.GetUtcNow(), cancellationToken));
    }
}
