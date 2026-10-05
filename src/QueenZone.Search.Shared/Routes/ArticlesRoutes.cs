using QueenZone.Data;

namespace QueenZone.Routing;

public static partial class ArticlesRoutes
{
    public const int ArchivePageSize = 20;

    /// <summary>Number of articles shown in the homepage "Featured" teaser section.</summary>
    public const int HomeFeaturedCount = 3;

    /// <summary>Canonical RSS 2.0 feed for published archive + community articles.</summary>
    public const string FeedPath = "/articles/feed.rss";

    public static int GetArchiveTotalPages(int publishedCount, int pageSize = ArchivePageSize) =>
        ArchivePagination.GetTotalPages(publishedCount, pageSize);

    public static string GetArchiveCanonicalPath(int page) =>
        page <= 1 ? "/articles" : $"/articles/page/{page}";

    public static string GetArchivePageTitle(int page) =>
        page <= 1 ? "QueenZone articles" : $"QueenZone articles – Page {page}";

    public static ArchivePaginationViewModel? GetArchivePaginationViewModel(int currentPage, int totalPages) =>
        ArchivePagination.BuildViewModel("Articles archive pagination", currentPage, totalPages, GetArchiveCanonicalPath);

    public static string GetTaggedListPath(string tag, int page)
    {
        var encoded = Uri.EscapeDataString(tag);
        return page <= 1 ? $"/articles?tag={encoded}" : $"/articles?tag={encoded}&page={page}";
    }

    public static ArchivePaginationViewModel? GetTaggedPaginationViewModel(
        string tag,
        int currentPage,
        int totalPages) =>
        ArchivePagination.BuildViewModel(
            "Articles archive pagination",
            currentPage,
            totalPages,
            page => GetTaggedListPath(tag, page));

    public static int ResolveArchiveTotalPages(int currentPage, int itemCount, int publishedCount, int totalPages) =>
        ArchivePagination.ResolveTotalPages(currentPage, itemCount, publishedCount, totalPages, ArchivePageSize);

    public static string GetArticleDetailPath(int id, string title) =>
        $"/articles/{id}/{NewsSlug.Slugify(title)}";

    public static string GetArticleDetailPath(ArticleItem item) =>
        GetArticleDetailPath(item.Id, item.Title);

    public static string GetCommunityArticleDetailPath(string slug) => $"/articles/{slug}";
}
