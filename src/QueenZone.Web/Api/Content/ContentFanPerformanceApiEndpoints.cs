using QueenZone.Data;
using QueenZone.Storage;

namespace QueenZone.Web;

/// <summary>
/// Public fan-performance list, detail, and member-gated audio routes.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentFanPerformanceApiEndpoints
{
    internal static void MapContentFanPerformanceApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/fan-performances", GetFanPerformancesAsync)
            .WithName("GetContentFanPerformances")
            .WithSummary("Paged list of public fan-stage recordings. Duration is MPEG metadata when available.")
            .Produces<ApiPagedResponse<FanPerformanceDto>>();

        group.MapGet("/fan-performances/{id:int}", GetFanPerformanceDetailAsync)
            .WithName("GetContentFanPerformanceDetail")
            .WithSummary("A single public fan-stage recording, including duration and the member-gated audio path.")
            .Produces<FanPerformanceDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/fan-performances/{id:int}/audio", GetFanPerformanceAudioAsync)
            .WithName("GetContentFanPerformanceAudio")
            .WithSummary("Member-gated audio stream. Same blob and range support as /fan-performances/{id}/audio.")
            .RequireAuthorization(MemberAuthenticationSchemes.MobileMemberPolicy)
            .RequireRateLimiting(FanPerformanceRateLimitingOptions.AudioPolicy)
            .Produces(StatusCodes.Status200OK, contentType: "audio/mpeg")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    internal static async Task<IResult> GetFanPerformancesAsync(
        PublicQueryCacheService publicQueryCache,
        FanPerformanceCreditResolver creditResolver,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var items = await creditResolver.EnrichAsync(
            await publicQueryCache.GetFanPerformancePageAsync(request.Page, request.PageSize, cancellationToken),
            cancellationToken);
        var totalCount = await publicQueryCache.GetFanPerformanceVisibleCountAsync(cancellationToken);
        var response = ApiPagedResponse<FanPerformanceDto>.Create(
            ContentApiMapper.ToFanPerformanceDtos(items),
            request.Page,
            request.PageSize,
            totalCount);

        return Results.Ok(response);
    }

    internal static async Task<IResult> GetFanPerformanceDetailAsync(
        PublicQueryCacheService publicQueryCache,
        FanPerformanceDurationResolver durationResolver,
        FanPerformanceCreditResolver creditResolver,
        int id,
        CancellationToken cancellationToken)
    {
        var performance = await creditResolver.EnrichOneAsync(
            await publicQueryCache.GetFanPerformanceByIdAsync(id, cancellationToken),
            cancellationToken);
        if (performance is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"No public fan performance with id '{id}'.");
        }

        var duration = await durationResolver.ResolveAsync(performance, cancellationToken);
        return Results.Ok(ContentApiMapper.ToFanPerformanceDto(performance, duration));
    }

    internal static Task<IResult> GetFanPerformanceAudioAsync(
        int id,
        IFanPerformanceRepository fanPerformanceRepository,
        IBlobUploadService blobUploadService,
        CancellationToken cancellationToken) =>
        FanPerformanceEndpoints.ServeAudioAsync(
            id,
            fanPerformanceRepository,
            blobUploadService,
            cancellationToken);
}
