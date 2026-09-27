using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web;

public static class NotificationPreferencesApiEndpoints
{
    public const string Path = "/api/v1/me/notification-preferences";

    public const string ForumWatchSummary =
        "Current notification category toggles. forumReply is a master mute; forum reply pushes also require Watching the topic (#735).";

    public static void MapNotificationPreferencesApiEndpoints(this WebApplication app)
    {
        var group = app.MapApiV1Group("/api/v1/me", "Notifications")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .DisableAntiforgery();

        group.MapGet("/notification-preferences", GetAsync)
            .WithName("GetNotificationPreferences")
            .WithSummary(ForumWatchSummary)
            .Produces<NotificationPreferencesResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPatch("/notification-preferences", PatchAsync)
            .WithName("PatchNotificationPreferences")
            .WithSummary(ForumWatchSummary)
            .Accepts<NotificationPreferencePatchRequest>("application/json")
            .Produces<NotificationPreferencesResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    internal static async Task<IResult> GetAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        [FromServices] IMemberAccountRepository memberAccountRepository,
        [FromServices] INotificationPreferenceRepository notificationPreferenceRepository,
        CancellationToken cancellationToken)
    {
        var memberId = await RequireAccountAsync(user, memberAccountRepository, cancellationToken);
        if (memberId.Failure is not null)
        {
            return memberId.Failure;
        }

        var preferences = await notificationPreferenceRepository.GetAsync(memberId.Value, cancellationToken);
        httpContext.Response.Headers.CacheControl = "no-store";
        return Results.Ok(NotificationPreferencesResponse.From(preferences));
    }

    internal static async Task<IResult> PatchAsync(
        ClaimsPrincipal user,
        [FromServices] IMemberAccountRepository memberAccountRepository,
        [FromServices] INotificationPreferenceRepository notificationPreferenceRepository,
        NotificationPreferencePatchRequest? request,
        CancellationToken cancellationToken)
    {
        var memberId = await RequireAccountAsync(user, memberAccountRepository, cancellationToken);
        if (memberId.Failure is not null)
        {
            return memberId.Failure;
        }

        if (request is null)
        {
            return BadRequest("A JSON body is required.");
        }

        var patch = request.ToPatch();
        if (patch.IsEmpty)
        {
            return BadRequest("Provide at least one notification preference.");
        }

        var saved = await notificationPreferenceRepository.ApplyAsync(memberId.Value, patch, cancellationToken);
        return Results.Ok(NotificationPreferencesResponse.From(saved));
    }

    private static async Task<(Guid Value, IResult? Failure)> RequireAccountAsync(
        ClaimsPrincipal user,
        IMemberAccountRepository memberAccountRepository,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return (Guid.Empty, Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized"));
        }

        var account = await memberAccountRepository.FindByIdAsync(memberId.Value, cancellationToken);
        if (account is null)
        {
            return (Guid.Empty, Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "The access token is invalid or expired."));
        }

        return (memberId.Value, null);
    }

    private static IResult BadRequest(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail);
}
