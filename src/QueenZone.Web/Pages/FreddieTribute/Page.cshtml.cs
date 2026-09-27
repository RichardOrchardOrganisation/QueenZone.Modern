using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.FreddieTribute;

public sealed class TributePageModel(
    IFreddieTributeRepository tributeRepository,
    PublicQueryCacheService publicQueryCache) : FreddieTributeArchivePageModel(tributeRepository, publicQueryCache)
{
    public async Task<IActionResult> OnGetAsync(int pageNumber, CancellationToken cancellationToken) =>
        await LoadArchivePageAsync(pageNumber, cancellationToken);
}
