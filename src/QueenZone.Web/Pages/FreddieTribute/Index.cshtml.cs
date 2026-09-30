using Microsoft.AspNetCore.Mvc;
using QueenZone.Data;

namespace QueenZone.Web.Pages.FreddieTribute;

public sealed class IndexModel(
    IFreddieTributeRepository tributeRepository,
    PublicQueryCacheService publicQueryCache) : FreddieTributeArchivePageModel(tributeRepository, publicQueryCache)
{
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) =>
        await LoadArchivePageAsync(1, cancellationToken);
}

