using QueenZone.Data;

namespace QueenZone.Web;

/// <summary>
/// Freddie Mercury tribute list route.
/// Registered by <see cref="ContentApiEndpoints.MapContentApiEndpoints"/> on the
/// existing <c>/api/v1/content</c> group so paths and route names stay unchanged.
/// </summary>
public static class ContentFreddieTributeApiEndpoints
{
    internal static void MapContentFreddieTributeApiEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/freddietribute", GetFreddieTributesAsync)
            .WithName("GetContentFreddieTributes")
            .WithSummary("Paged list of Freddie Mercury tributes.")
            .Produces<ApiPagedResponse<FreddieTributeDto>>();
    }

    internal static async Task<IResult> GetFreddieTributesAsync(
        IFreddieTributeRepository tributeRepository,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        var request = ApiPagination.Normalize(page, pageSize);
        var tributePage = await tributeRepository.GetPageAsync(request.Page, request.PageSize, cancellationToken);

        var response = ApiPagedResponse<FreddieTributeDto>.Create(
            ContentApiMapper.ToFreddieTributeDtos(tributePage.Items),
            request.Page,
            request.PageSize,
            tributePage.TotalCount);

        return Results.Ok(response);
    }
}
