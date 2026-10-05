namespace QueenZone.Routing;

/// <summary>
/// Shared pagination math and pagination-nav view-model building used by the
/// News, Articles, and Forum archive/category/topic pages. Rendering happens
/// in the <c>_ArchivePagination</c> Razor partial.
/// </summary>
public static class ArchivePagination
{
    /// <summary>First/last cluster size when the current page is near either end of a long archive.</summary>
    private const int EdgePageCount = 4;

    public static int GetTotalPages(int totalCount, int pageSize)
    {
        if (totalCount <= 0)
        {
            return 0;
        }

        return (totalCount + pageSize - 1) / pageSize;
    }

    public static int ResolveTotalPages(int currentPage, int itemCount, int publishedCount, int totalPages, int pageSize)
    {
        if (itemCount == pageSize && totalPages <= currentPage)
        {
            return Math.Max(totalPages, currentPage + 1);
        }

        if (publishedCount > 0)
        {
            return totalPages;
        }

        return itemCount == 0 ? 0 : Math.Max(totalPages, currentPage);
    }

    public static ArchivePaginationViewModel? BuildViewModel(
        string ariaLabel,
        int currentPage,
        int totalPages,
        Func<int, string> pageHref)
    {
        if (totalPages <= 1)
        {
            return null;
        }

        var pages = new List<ArchivePaginationPageLink>();

        foreach (var pageNumber in GetVisiblePageNumbers(currentPage, totalPages))
        {
            if (pageNumber is null)
            {
                pages.Add(new ArchivePaginationPageLink());
            }
            else if (pageNumber == currentPage)
            {
                pages.Add(new ArchivePaginationPageLink { PageNumber = pageNumber, IsCurrent = true });
            }
            else
            {
                pages.Add(new ArchivePaginationPageLink { PageNumber = pageNumber, Href = pageHref(pageNumber.Value) });
            }
        }

        return new ArchivePaginationViewModel
        {
            AriaLabel = ariaLabel,
            CurrentPage = currentPage,
            TotalPages = totalPages,
            PreviousHref = currentPage > 1 ? pageHref(currentPage - 1) : null,
            NextHref = currentPage < totalPages ? pageHref(currentPage + 1) : null,
            Pages = pages,
        };
    }

    private static IEnumerable<int?> GetVisiblePageNumbers(int currentPage, int totalPages)
    {
        // Two edge clusters with no gap would overlap or abut — list every page.
        if (totalPages <= EdgePageCount * 2)
        {
            for (var page = 1; page <= totalPages; page++)
            {
                yield return page;
            }

            yield break;
        }

        if (currentPage <= EdgePageCount || currentPage > totalPages - EdgePageCount)
        {
            for (var page = 1; page <= EdgePageCount; page++)
            {
                yield return page;
            }

            yield return null;

            for (var page = totalPages - EdgePageCount + 1; page <= totalPages; page++)
            {
                yield return page;
            }

            yield break;
        }

        yield return 1;

        var start = currentPage - 1;
        var end = currentPage + 1;

        yield return null;

        for (var page = Math.Max(2, start); page <= Math.Min(totalPages - 1, end); page++)
        {
            yield return page;
        }

        if (end < totalPages - 1)
        {
            yield return null;
        }

        yield return totalPages;
    }
}
