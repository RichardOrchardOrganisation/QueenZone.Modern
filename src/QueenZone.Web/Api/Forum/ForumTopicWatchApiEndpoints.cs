using System.Security.Claims;

namespace QueenZone.Web;

/// <summary>
/// Topic Watch routes under <c>/api/v1/forum</c>. Registered by
/// <see cref="ForumApiEndpoints.MapForumApiEndpoints"/> on the existing forum
/// group so paths and route names stay unchanged.
/// </summary>
public static class ForumTopicWatchApiEndpoints
{
    internal static void MapForumTopicWatchApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/topics/{id:int}/watch", GetTopicWatchAsync)
            .WithName("GetForumTopicWatch")
            .WithSummary("Whether the signed-in member is Watching this public topic. Watch is the opt-in for forum reply pushes.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .Produces<ForumTopicWatchDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/topics/{id:int}/watch", WatchTopicAsync)
            .WithName("WatchForumTopic")
            .WithSummary("Watch a public topic. Idempotent. Does not auto-watch on post.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .Produces<ForumTopicWatchDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapDelete("/topics/{id:int}/watch", UnwatchTopicAsync)
            .WithName("UnwatchForumTopic")
            .WithSummary("Stop Watching a public topic. Idempotent when not currently Watching.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .Produces<ForumTopicWatchDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    internal static async Task<IResult> GetTopicWatchAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        int id,
        TopicWatchService topicWatchService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var status = await topicWatchService.GetStatusAsync(memberId.Value, id, cancellationToken);
        if (status is null)
        {
            return ForumApiEndpoints.TopicNotFound(id);
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new ForumTopicWatchDto(status.Watching));
    }

    internal static async Task<IResult> WatchTopicAsync(
        ClaimsPrincipal user,
        int id,
        TopicWatchService topicWatchService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var status = await topicWatchService.WatchAsync(memberId.Value, id, cancellationToken);
        return status is null
            ? ForumApiEndpoints.TopicNotFound(id)
            : Results.Ok(new ForumTopicWatchDto(status.Watching));
    }

    internal static async Task<IResult> UnwatchTopicAsync(
        ClaimsPrincipal user,
        int id,
        TopicWatchService topicWatchService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var status = await topicWatchService.UnwatchAsync(memberId.Value, id, cancellationToken);
        return status is null
            ? ForumApiEndpoints.TopicNotFound(id)
            : Results.Ok(new ForumTopicWatchDto(status.Watching));
    }
}
