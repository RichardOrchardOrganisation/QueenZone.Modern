using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Topic poll read, vote, and close routes under <c>/api/v1/forum</c>.
/// Registered by <see cref="ForumApiEndpoints.MapForumApiEndpoints"/> on the
/// existing forum group so paths and route names stay unchanged.
/// </summary>
public static class ForumPollApiEndpoints
{
    internal static void MapForumPollApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/topics/{id:int}/poll", GetTopicPollAsync)
            .WithName("GetForumTopicPoll")
            .WithSummary("Poll on a public topic. Same vote-vs-results flags as the website topic page.")
            .Produces<ForumPollDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/topics/{id:int}/poll/vote", VoteTopicPollAsync)
            .WithName("VoteForumTopicPoll")
            .WithSummary("Cast one ballot on a topic poll. Same one-vote and closed rules as /forum/poll/{id}/vote.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .Accepts<ForumPollVoteRequestDto>("application/json")
            .Produces<ForumPollDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/topics/{id:int}/poll/close", CloseTopicPollAsync)
            .WithName("CloseForumTopicPoll")
            .WithSummary("Close a topic poll. Same author-or-admin rule as /forum/poll/{id}/close.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .Produces<ForumPollDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    internal static async Task<IResult> GetTopicPollAsync(
        HttpContext httpContext,
        IForumRepository forumRepository,
        IForumPollRepository pollRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadPublicPollAsync(
            forumRepository,
            pollRepository,
            id,
            await TryGetBearerMemberIdAsync(httpContext),
            cancellationToken);
        return loaded.Result ?? Results.Ok(ForumApiMapper.ToPoll(loaded.Poll!));
    }

    internal static async Task<IResult> VoteTopicPollAsync(
        ClaimsPrincipal user,
        IForumRepository forumRepository,
        IForumPollRepository pollRepository,
        int id,
        ForumPollVoteRequestDto? request,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var loaded = await LoadPublicPollAsync(
            forumRepository,
            pollRepository,
            id,
            memberId,
            cancellationToken);
        if (loaded.Result is not null)
        {
            return loaded.Result;
        }

        var optionIds = ForumPollVoteMapper.ParseOptionIds(request?.OptionIds, request?.OptionId);
        try
        {
            await pollRepository.CastVoteAsync(loaded.Poll!.PollId, memberId.Value, optionIds, cancellationToken);
        }
        catch (ForumPollVoteException ex)
        {
            return ForumPollVoteMapper.ToProblemResult(ex);
        }

        return await ReloadPollAsync(pollRepository, id, memberId.Value, cancellationToken);
    }

    internal static async Task<IResult> CloseTopicPollAsync(
        ClaimsPrincipal user,
        IForumRepository forumRepository,
        IForumPollRepository pollRepository,
        int id,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var loaded = await LoadPublicPollAsync(
            forumRepository,
            pollRepository,
            id,
            memberId,
            cancellationToken);
        if (loaded.Result is not null)
        {
            return loaded.Result;
        }

        try
        {
            // Mobile JWTs are never treated as admin (see json-api-v1.md).
            await pollRepository.ClosePollAsync(
                loaded.Poll!.PollId,
                memberId.Value,
                isAdmin: false,
                cancellationToken);
        }
        catch (ForumPollVoteException ex)
        {
            return ForumPollVoteMapper.ToProblemResult(ex);
        }

        return await ReloadPollAsync(pollRepository, id, memberId.Value, cancellationToken);
    }

    private static async Task<(ForumPollResults? Poll, IResult? Result)> LoadPublicPollAsync(
        IForumRepository forumRepository,
        IForumPollRepository pollRepository,
        int topicId,
        Guid? viewerMemberId,
        CancellationToken cancellationToken)
    {
        var topicPage = await forumRepository.GetTopicPostsPageAsync(topicId, 1, 1, cancellationToken);
        if (topicPage is null)
        {
            return (null, ForumApiEndpoints.TopicNotFound(topicId));
        }

        var poll = await pollRepository.GetPollWithResultsAsync(
            topicId,
            viewerMemberId,
            viewerIsAdmin: false,
            cancellationToken);
        if (poll is null)
        {
            return (null, PollNotFound(topicId));
        }

        return (poll, null);
    }

    private static async Task<IResult> ReloadPollAsync(
        IForumPollRepository pollRepository,
        int topicId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var poll = await pollRepository.GetPollWithResultsAsync(
            topicId,
            memberId,
            viewerIsAdmin: false,
            cancellationToken);
        return poll is null
            ? PollNotFound(topicId)
            : Results.Ok(ForumApiMapper.ToPoll(poll));
    }

    private static async Task<Guid?> TryGetBearerMemberIdAsync(HttpContext httpContext)
    {
        if (!httpContext.Request.Headers.ContainsKey("Authorization"))
        {
            return null;
        }

        var bearer = await httpContext.AuthenticateAsync(MemberAuthenticationSchemes.MembersBearer);
        return bearer.Succeeded ? ForumMember.GetMemberId(bearer.Principal) : null;
    }

    private static IResult PollNotFound(int topicId) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: $"No poll on public forum topic '{topicId}'.");
}
