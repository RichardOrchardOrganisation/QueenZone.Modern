using System.Security.Claims;
using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Push device token registration for the mobile app (#757). Devices are stored per member,
/// tagged with their push provider (APNs/FCM). Consumed by dispatch (#759) and stale-token
/// cleanup (#760). Requires <see cref="MemberAuthenticationSchemes.MobileMemberPolicy"/>.
/// </summary>
public static class DevicesApiEndpoints
{
    public const int MaxDeviceIdLength = 200;
    public const int MaxTokenLength = 4000;

    public static void MapDevicesApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/notifications")
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Notifications")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .DisableAntiforgery();

        group.MapPost("/devices", RegisterDeviceAsync)
            .WithName("RegisterDevice")
            .WithSummary("Register or update this device's push token. Idempotent by deviceId.")
            .Accepts<DeviceRegisterRequest>("application/json")
            .Produces<DeviceRegisteredResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapDelete("/devices/{deviceId}", UnregisterDeviceAsync)
            .WithName("UnregisterDevice")
            .WithSummary("Unregister this device's push token (sign-out, permission revoked, or settings toggle).")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    internal static async Task<IResult> RegisterDeviceAsync(
        ClaimsPrincipal user,
        IDeviceTokenRepository deviceTokenRepository,
        DeviceRegisterRequest? request,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var deviceId = request?.DeviceId?.Trim();
        var token = request?.Token?.Trim();
        if (string.IsNullOrEmpty(deviceId) || deviceId.Length > MaxDeviceIdLength)
        {
            return BadRequest("Provide a deviceId (max 200 characters).");
        }

        if (request?.Platform is null)
        {
            return BadRequest("Provide a platform (apns or fcm).");
        }

        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
        {
            return BadRequest("Provide a push token.");
        }

        var now = DateTime.UtcNow;
        var stored = await deviceTokenRepository.UpsertAsync(
            DeviceTokenMapper.ToEntity(memberId, deviceId, request.Platform.Value, token, now),
            cancellationToken);

        return Results.Ok(DeviceTokenMapper.ToRegisteredResponse(stored));
    }

    internal static async Task<IResult> UnregisterDeviceAsync(
        ClaimsPrincipal user,
        IDeviceTokenRepository deviceTokenRepository,
        string deviceId,
        CancellationToken cancellationToken)
    {
        var memberId = RequireMemberId(user, out var unauthorized);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var removed = await deviceTokenRepository.DeleteByDeviceIdAsync(memberId, deviceId, cancellationToken);
        if (!removed)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: "No registered device matches that deviceId.");
        }

        return Results.NoContent();
    }

    private static Guid RequireMemberId(ClaimsPrincipal user, out IResult? failure)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            failure = Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
            return Guid.Empty;
        }

        failure = null;
        return memberId.Value;
    }

    private static IResult BadRequest(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail);
}
