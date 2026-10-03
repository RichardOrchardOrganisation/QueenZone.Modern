using QueenZone.Data;

namespace QueenZone.Routing;

public static class ForumRoutes
{
    public const int TopicsPageSize = 25;

    public const int PostsPageSize = 15;

    public const int RecentThreadsCount = 15;

    public static string GetCategoryPath(ForumCategorySummary category) =>
        category.DetailPath;

    public static string GetCategoryCanonicalPath(ForumCategorySummary category, int page = 1) =>
        GetCategoryCanonicalPath(category.Id, category.Name, page);

    public static string GetCategoryCanonicalPath(int id, string name, int page = 1)
    {
        var slug = NewsSlug.Slugify(name);
        return page <= 1 ? $"/forum/{id}/{slug}" : $"/forum/{id}/{slug}/page/{page}";
    }

    public static string GetNewThreadPath(ForumCategorySummary category) =>
        $"/forum/c/{NewsSlug.Slugify(category.Name)}/new-thread";

    public static string GetCategoryPageTitle(ForumCategorySummary category, int page) =>
        page <= 1 ? $"{category.Name} | QueenZone forum" : $"{category.Name} – Page {page} | QueenZone forum";

    public static string GetTopicPath(ForumThreadSummary topic) =>
        topic.DetailPath;

    public static string GetTopicCanonicalPath(ForumThreadHeader header, int page = 1) =>
        GetTopicCanonicalPath(header.TopicId, header.Title, page);

    public static string GetTopicCanonicalPath(int topicId, string title, int page = 1)
    {
        var slug = NewsSlug.Slugify(title);
        return page <= 1 ? $"/forum/topic/{topicId}/{slug}" : $"/forum/topic/{topicId}/{slug}/page/{page}";
    }

    /// <summary>
    /// Resolver for a legacy topic or reply ID (old <c>forum_topic_view.aspx?Q={id}</c> links).
    /// Redirects to <see cref="GetLegacyPostTargetPath"/>.
    /// </summary>
    public static string GetLegacyPostPath(int legacyPostId) =>
        $"/forum/goto/{legacyPostId}";

    /// <summary>Topic page holding the post, anchored to it unless it is the first post.</summary>
    public static string GetLegacyPostTargetPath(ForumLegacyPostLocation location)
    {
        var page = location.PostIndex / PostsPageSize + 1;
        var path = GetTopicCanonicalPath(location.TopicId, location.Title, page);
        return location.PostIndex == 0 ? path : $"{path}#post-{location.PostId}";
    }

    public static string GetTopicPageTitle(ForumThreadHeader header, int page) =>
        page <= 1 ? $"{header.Title} | {header.ForumName} | QueenZone forum"
            : $"{header.Title} – Page {page} | QueenZone forum";

    public static int GetPostsTotalPages(int totalCount, int pageSize = PostsPageSize) =>
        GetTopicsTotalPages(totalCount, pageSize);

    public static int GetTopicsTotalPages(int totalCount, int pageSize = TopicsPageSize)
    {
        if (totalCount <= 0)
        {
            return 0;
        }

        return (totalCount + pageSize - 1) / pageSize;
    }

    public static string FormatCount(long value) =>
        value >= 1_000_000
            ? $"{value / 1_000_000.0:0.#}M+"
            : value >= 1_000
                ? $"{value / 1_000.0:0.#}k+"
                : value.ToString("N0");

    public static ArchivePaginationViewModel? GetCategoryPaginationViewModel(
        ForumCategorySummary category,
        int currentPage,
        int totalPages) =>
        ArchivePagination.BuildViewModel(
            "Forum topics pagination",
            currentPage,
            totalPages,
            page => GetCategoryCanonicalPath(category, page));

    public static ArchivePaginationViewModel? GetTopicPaginationViewModel(
        ForumThreadHeader header,
        int currentPage,
        int totalPages) =>
        ArchivePagination.BuildViewModel(
            "Forum posts pagination",
            currentPage,
            totalPages,
            page => GetTopicCanonicalPath(header, page));
}
