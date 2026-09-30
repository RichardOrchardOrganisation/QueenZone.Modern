namespace QueenZone.Web;

/// <summary>
/// Shared <c>/api/v1</c> route-group setup, list/detail registration, and
/// paged-list / not-found results. Callers keep route names, summaries, status
/// codes, and detail strings so response shapes stay on the v1 contract.
/// </summary>
internal static class ApiV1EndpointHelpers
{
    /// <summary>
    /// OpenAPI document group for a versioned route prefix. Callers add
    /// authorization, rate limiting, and antiforgery in their existing order.
    /// </summary>
    public static RouteGroupBuilder MapApiV1Group(this WebApplication app, string prefix, string tag) =>
        app.MapGroup(prefix)
            .WithGroupName(ApiV1.OpenApiDocumentName)
            .WithTags(tag);

    public static RouteHandlerBuilder MapPagedList<TItem>(
        this RouteGroupBuilder group,
        string pattern,
        Delegate handler,
        string name,
        string summary) =>
        group.MapGet(pattern, handler)
            .WithName(name)
            .WithSummary(summary)
            .Produces<ApiPagedResponse<TItem>>();

    public static RouteHandlerBuilder MapAuthorizedPagedList<TItem>(
        this RouteGroupBuilder group,
        string pattern,
        Delegate handler,
        string name,
        string summary) =>
        group.MapPagedList<TItem>(pattern, handler, name, summary)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

    public static RouteHandlerBuilder MapDetail<TItem>(
        this RouteGroupBuilder group,
        string pattern,
        Delegate handler,
        string name,
        string summary) =>
        group.MapGet(pattern, handler)
            .WithName(name)
            .WithSummary(summary)
            .Produces<TItem>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    public static IResult NotFound(string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: detail);

    public static IResult OkPaged<TItem>(
        IReadOnlyList<TItem> items,
        int page,
        int pageSize,
        int totalCount) =>
        Results.Ok(ApiPagedResponse<TItem>.Create(items, page, pageSize, totalCount));

    public static IResult OkNoStorePaged<TItem>(
        HttpContext httpContext,
        IReadOnlyList<TItem> items,
        int page,
        int pageSize,
        int totalCount)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        return OkPaged(items, page, pageSize, totalCount);
    }

    /// <summary>
    /// Pages an already-loaded list with the default <see cref="ApiPagination"/> clamp,
    /// then maps only that page.
    /// </summary>
    public static IResult OkPagedSlice<TSource, TItem>(
        IReadOnlyList<TSource> source,
        int? page,
        int? pageSize,
        Func<IReadOnlyList<TSource>, IReadOnlyList<TItem>> map)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var pageItems = source
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();
        return OkPaged(map(pageItems), request.Page, request.PageSize, source.Count);
    }
}
