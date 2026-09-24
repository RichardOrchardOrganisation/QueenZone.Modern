using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Current Home poll read and vote routes.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentHomePollApiEndpoints
{
    internal static void MapContentHomePollApiEndpoints(this RouteGroupBuilder group)
    {
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
    }

    internal static async Task<IResult> GetHomePollAsync(
        HttpContext httpContext,
        IHomePollRepository homePollRepository,
        CancellationToken cancellationToken)
    {
        var poll = await homePollRepository.GetCurrentAsync(
            await ContentApiEndpoints.TryGetViewerMemberIdAsync(httpContext),
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
}
