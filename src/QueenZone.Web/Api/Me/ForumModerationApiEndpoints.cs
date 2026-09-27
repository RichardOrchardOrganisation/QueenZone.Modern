using System.Security.Claims;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// <c>/api/v1/me/forum</c> report, block, and moderation-state routes.
/// Registered by <see cref="ForumApiEndpoints.MapForumApiEndpoints"/> on the
/// existing moderation group so paths and route names stay unchanged.
/// </summary>
public static class ForumModerationApiEndpoints
{
    internal static void MapForumModerationApiEndpoints(this RouteGroupBuilder memberGroup)
    {
        memberGroup.MapPost("/posts/{postId:int}/report", ReportPostAsync)
            .WithName("ReportForumPost")
            .WithSummary("Report a visible forum post. Duplicate submissions return the existing report.")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .Accepts<ForumPostReportRequestDto>("application/json")
            .Produces<ForumPostReportResponseDto>(StatusCodes.Status201Created)
            .Produces<ForumPostReportResponseDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        memberGroup.MapPost("/posts/{postId:int}/block", BlockPostAuthorAsync)
            .WithName("BlockForumPostAuthor")
            .WithSummary("Block the linked member who authored a visible forum post.")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        memberGroup.MapPost("/posts/{postId:int}/unblock", UnblockPostAuthorAsync)
            .WithName("UnblockForumPostAuthor")
            .WithSummary("Unblock the linked member who authored a visible forum post.")
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        memberGroup.MapPost("/posts/moderation-state", GetPostModerationStateAsync)
            .WithName("GetForumPostModerationState")
            .WithSummary("Return report and block state for the supplied forum post page.")
            .Accepts<ForumPostModerationStateRequestDto>("application/json")
            .Produces<ForumPostModerationStateDto>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }

    internal static async Task<IResult> ReportPostAsync(
        ClaimsPrincipal user,
        int postId,
        ForumPostReportRequestDto request,
        ForumPostReportService reportService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        var result = await reportService.ReportAsync(memberId.Value, postId, request.Category, request.Details, cancellationToken);
        if (!result.Succeeded)
        {
            var notFound = result.ErrorMessage == ForumPostReportText.PostNotFound;
            return Results.Problem(
                statusCode: notFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                title: notFound ? "Not Found" : "Bad Request",
                detail: result.ErrorMessage);
        }

        var dto = new ForumPostReportResponseDto(result.ReportId!.Value, PrivateMessageReportStatus.Open, result.AlreadyReported);
        return result.AlreadyReported ? Results.Ok(dto) : Results.Created($"/api/v1/me/forum/reports/{dto.ReportId}", dto);
    }

    internal static async Task<IResult> BlockPostAuthorAsync(
        ClaimsPrincipal user,
        int postId,
        ForumPostReportService reportService,
        PrivateMessageService privateMessageService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        var post = await reportService.GetVisiblePostAsync(postId, cancellationToken);
        if (post?.AuthorMemberId is not Guid authorMemberId)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
                detail: "This post does not belong to an active member account.");
        }

        var result = await privateMessageService.BlockAsync(memberId.Value, authorMemberId, cancellationToken);
        return result.Succeeded
            ? Results.NoContent()
            : Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: result.ErrorMessage);
    }

    internal static async Task<IResult> UnblockPostAuthorAsync(
        ClaimsPrincipal user,
        int postId,
        ForumPostReportService reportService,
        PrivateMessageService privateMessageService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Unauthorized();
        }

        var post = await reportService.GetVisiblePostAsync(postId, cancellationToken);
        if (post?.AuthorMemberId is not Guid authorMemberId)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
                detail: "This post does not belong to an active member account.");
        }

        await privateMessageService.UnblockAsync(memberId.Value, authorMemberId, cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<IResult> GetPostModerationStateAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        ForumPostModerationStateRequestDto request,
        IForumPostReportRepository reports,
        PrivateMessageService privateMessageService,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Unauthorized();
        }
        if (request.PostIds.Count > ForumRoutes.PostsPageSize || request.AuthorMemberIds.Count > ForumRoutes.PostsPageSize)
        {
            return Results.BadRequest();
        }

        var reported = await reports.GetReportedPostIdsAsync(memberId.Value, request.PostIds, cancellationToken);
        var blocked = await privateMessageService.ListBlockedMemberIdsAsync(memberId.Value, request.AuthorMemberIds, cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new ForumPostModerationStateDto(reported.ToList(), blocked.ToList()));
    }
}
