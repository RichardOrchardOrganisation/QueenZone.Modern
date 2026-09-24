using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// The public live-activity count of forum replies posted today.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentLiveActivityApiEndpoints
{
    internal static void MapContentLiveActivityApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/live-activity", GetLiveActivityAsync)
            .WithName("GetContentLiveActivity")
            .WithSummary("Count of new forum replies posted today. No presence/reading tracking exists; this is the only honest live signal available.")
            .Produces<LiveActivitySummaryDto>();
    }

    internal static async Task<IResult> GetLiveActivityAsync(
        PublicQueryCacheService publicQueryCache,
        CancellationToken cancellationToken)
    {
        var count = await publicQueryCache.GetLiveActivityNewForumRepliesTodayAsync(cancellationToken);
        return Results.Ok(new LiveActivitySummaryDto(count));
    }
}
