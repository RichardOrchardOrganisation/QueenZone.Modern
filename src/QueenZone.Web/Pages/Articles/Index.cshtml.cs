using Microsoft.AspNetCore.Mvc;

namespace QueenZone.Web.Pages.Articles;

public sealed class IndexModel(PublicQueryCacheService publicQueryCache) : ArticlesArchivePageModel(publicQueryCache)
{
    public async Task<IActionResult> OnGetAsync(
        [FromQuery(Name = "cp")] int? communityPage,
        [FromQuery(Name = "tag")] string? tag,
        [FromQuery(Name = "page")] int listPage = 1,
        CancellationToken cancellationToken = default)
    {
        if (communityPage is not null)
        {
            var location = string.IsNullOrWhiteSpace(tag)
                ? "/articles"
                : ArticlesRoutes.GetTaggedListPath(tag, 1);
            return RedirectPermanent(location);
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            return await LoadArchivePageAsync(1, cancellationToken);
        }

        return await LoadArchivePageAsync(listPage, cancellationToken, tag);
    }
}
