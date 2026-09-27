using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Published history timeline, anchor, event detail, and on-this-day routes.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentTimelineApiEndpoints
{
    internal static void MapContentTimelineApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapPagedList<TimelineEventDto>(
            "/timeline",
            GetTimelineEventsAsync,
            "GetContentTimelineEvents",
            "Paged list of published history timeline events, in date order.");

        group.MapPagedList<TimelineEventDto>(
            "/timeline/anchor/{id:int}",
            GetTimelineAnchorAsync,
            "GetContentTimelineAnchor",
            "The cached timeline page containing a published event, for direct links.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDetail<TimelineEventDto>(
            "/timeline/{id:int}",
            GetTimelineEventDetailAsync,
            "GetContentTimelineEventDetail",
            "A single published history timeline event by id. Unpublished or missing events return 404.");

        group.MapGet("/on-this-day", GetOnThisDayAsync)
            .WithName("GetContentOnThisDay")
            .WithSummary("The single most notable published history event for today's date, with a +/-7 day fallback when none. Matches the website home page.")
            .Produces<TimelineEventDto?>();
    }

    internal static async Task<IResult> GetTimelineEventsAsync(
        PublicQueryCacheService publicQueryCache,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var events = await GetOrderedTimelineAsync(publicQueryCache, cancellationToken);
        return ApiV1EndpointHelpers.OkPagedSlice(
            events,
            page,
            pageSize,
            ContentApiMapper.ToTimelineEvents);
    }

    internal static async Task<IResult> GetTimelineAnchorAsync(
        PublicQueryCacheService publicQueryCache,
        int id,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(1, pageSize, 100);
        var events = await GetOrderedTimelineAsync(publicQueryCache, cancellationToken);
        var index = events.FindIndex(item => item.Id == id);
        if (index < 0)
        {
            return ApiV1EndpointHelpers.NotFound($"No published timeline event with id '{id}'.");
        }

        var page = index / request.PageSize + 1;
        var items = events.Skip((page - 1) * request.PageSize).Take(request.PageSize).ToList();
        return ApiV1EndpointHelpers.OkPaged(
            ContentApiMapper.ToTimelineEvents(items), page, request.PageSize, events.Count);
    }

    private static async Task<List<QueenHistoryEvent>> GetOrderedTimelineAsync(
        PublicQueryCacheService publicQueryCache,
        CancellationToken cancellationToken) =>
        (await publicQueryCache.GetAllPublishedHistoryEventsAsync(cancellationToken))
            .OrderBy(item => item.EventDate)
            .ThenByDescending(item => item.Importance)
            .ThenBy(item => item.Id)
            .ToList();

    internal static async Task<IResult> GetTimelineEventDetailAsync(
        PublicQueryCacheService publicQueryCache,
        int id,
        CancellationToken cancellationToken)
    {
        var historyEvent = (await publicQueryCache.GetAllPublishedHistoryEventsAsync(cancellationToken))
            .FirstOrDefault(item => item.Id == id);
        if (historyEvent is null)
        {
            return ApiV1EndpointHelpers.NotFound($"No published timeline event with id '{id}'.");
        }

        return Results.Ok(ContentApiMapper.ToTimelineEvent(historyEvent));
    }

    internal static async Task<IResult> GetOnThisDayAsync(
        PublicQueryCacheService publicQueryCache,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var events = await publicQueryCache.GetOnThisDayAsync(today, 3, cancellationToken);
        if (events.Count == 0)
        {
            events = await publicQueryCache.GetAroundThisDayAsync(today, 7, 3, cancellationToken);
        }

        // ASP.NET Core Ok(null) / Json(null) write an empty 200. The contract is JSON null.
        TimelineEventDto? payload = events.Count > 0 ? ContentApiMapper.ToTimelineEvent(events[0]) : null;
        return payload is null
            ? Results.Content("null", "application/json")
            : Results.Ok(payload);
    }
}
