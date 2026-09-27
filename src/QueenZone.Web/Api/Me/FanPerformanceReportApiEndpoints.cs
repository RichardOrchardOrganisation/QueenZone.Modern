using System.Security.Claims;
using QueenZone.Data;

namespace QueenZone.Web;

public static class FanPerformanceReportApiEndpoints
{
    public const string Path = "/api/v1/me/fan-performances";

    public static string ReportPath(int stageId) => $"{Path}/{stageId}/report";

    public static void MapFanPerformanceReportApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(Path)
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags("Me")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(QueenZoneRateLimitPolicies.AuthenticatedWrite)
            .DisableAntiforgery();

        group.MapPost("/{id:int}/report", CreateReportAsync)
            .WithName("ReportFanPerformance")
            .WithSummary("Report a published fan performance. Idempotent when this member already has an open report.")
            .Accepts<FanPerformanceReportRequestDto>("application/json")
            .Produces<FanPerformanceReportCreatedDto>(StatusCodes.Status201Created)
            .Produces<FanPerformanceReportCreatedDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    internal static async Task<IResult> CreateReportAsync(
        HttpContext httpContext,
        ClaimsPrincipal user,
        FanPerformanceReportService reportService,
        int id,
        FanPerformanceReportRequestDto? request,
        CancellationToken cancellationToken)
    {
        var memberId = ForumMember.GetMemberId(user);
        if (memberId is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized");
        }

        var result = await reportService.CreateAsync(
            memberId.Value,
            id,
            request?.Reason,
            cancellationToken);
        if (!result.Succeeded || result.ReportId is null)
        {
            var notFound = string.Equals(result.Error, "Fan performance was not found.", StringComparison.Ordinal);
            return Results.Problem(
                statusCode: notFound ? StatusCodes.Status404NotFound : StatusCodes.Status400BadRequest,
                title: notFound ? "Not Found" : "Bad Request",
                detail: result.Error);
        }

        httpContext.Response.Headers.CacheControl = "no-store";
        var dto = new FanPerformanceReportCreatedDto(result.ReportId.Value, result.AlreadyReported);
        return result.AlreadyReported
            ? Results.Ok(dto)
            : Results.Created(ReportPath(id), dto);
    }
}

public sealed record FanPerformanceReportRequestDto(string? Reason);

public sealed record FanPerformanceReportCreatedDto(Guid ReportId, bool AlreadyReported);
