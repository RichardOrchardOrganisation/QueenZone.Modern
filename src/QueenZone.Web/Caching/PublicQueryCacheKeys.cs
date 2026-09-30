namespace QueenZone.Web;

/// <summary>
/// Centralized key conventions for <see cref="PublicQueryCacheService"/>.
/// News and article entries are versioned so invalidation does not depend on
/// hard-coded caller counts or archive page shapes.
/// </summary>
public static class PublicQueryCacheKeys
{
    public const string Prefix = "public-query";

    public const string NewsVersion = Prefix + ":news:version";

    public const string LatestNewsSegment = Prefix + ":news:latest";

    public const string NewsPublishedCountSegment = Prefix + ":news:published-count";

    public const string NewsArchiveSegment = Prefix + ":news:archive";

    public const string ArticleVersion = Prefix + ":articles:version";

    public const string ArticlePublishedCountSegment = Prefix + ":articles:published-count";

    public const string LatestArticlesSegment = Prefix + ":articles:latest";

    public const string LatestCommunityArticlesSegment = Prefix + ":articles:community-latest";

    public const string ArticlesArchiveSegment = Prefix + ":articles:archive";

    public const string ForumCategories = Prefix + ":forum:categories";

    public const string ForumThreadCount = Prefix + ":forum:thread-count";

    public const string ForumRecentThreadsSegment = Prefix + ":forum:recent-threads";

    public const string ForumLegacyDiscographyThreads = Prefix + ":forum:legacy-discography-threads";

    public const string PhotoVersion = Prefix + ":photo:version";

    public const string FanPerformanceVersion = Prefix + ":fan-performance:version";

    public const string FanPerformancePageSegment = Prefix + ":fan-performance:page";

    public const string FanPerformanceCountSegment = Prefix + ":fan-performance:count";

    public const string FanPerformanceByIdSegment = Prefix + ":fan-performance:by-id";

    public const string HistoryVersion = Prefix + ":history:version";

    public const string HistoryAllPublishedSegment = Prefix + ":history:all-published";

    public const string PhotoCategoriesSegment = Prefix + ":photo:categories";

    public const string FreddieFeaturedTributeId = Prefix + ":freddie:featured-id";

    public const string PhotoCategoryPageSegment = Prefix + ":photo:category-page";

    public const string LiveActivityNewForumReplies = Prefix + ":live-activity:new-forum-replies";

    public const string PublishedQuotes = Prefix + ":quotes:published";

    public const string PublishedTrivia = Prefix + ":trivia:published";

    public const string BiographyChapters = Prefix + ":biography:chapters";

    public const string DiscographyAlbums = Prefix + ":discography:albums";

    public static string DiscographyAlbum(int id) => $"{Prefix}:discography:album:{id}";

    public static string LatestNews(string version, int count) =>
        $"{LatestNewsSegment}:v{version}:{count}";

    public static string NewsPublishedCount(string version) =>
        $"{NewsPublishedCountSegment}:v{version}";

    public static string NewsPublishedCount(string version, int? decade, int? year) =>
        $"{NewsPublishedCountSegment}:v{version}:decade={decade}|year={year}";

    public static string NewsArchivePage(string version, int page, int pageSize, int? decade, int? year) =>
        $"{NewsArchiveSegment}:v{version}:{page}:{pageSize}:decade={decade}|year={year}";

    public static string ArticlePublishedCount(string version) =>
        $"{ArticlePublishedCountSegment}:v{version}";

    public static string LatestArticles(string version, int count) =>
        $"{LatestArticlesSegment}:v{version}:{count}";

    public static string LatestCommunityArticles(string version, int count) =>
        $"{LatestCommunityArticlesSegment}:v{version}:{count}";

    public static string ArticlesArchivePage(string version, int page, int pageSize) =>
        $"{ArticlesArchiveSegment}:v{version}:{page}:{pageSize}";

    public static string PhotoCategories(string version) =>
        $"{PhotoCategoriesSegment}:v{version}";

    public static string FreddiePhotoSample(string version, int catId) =>
        $"{Prefix}:freddie:photo-sample:v{version}:{catId}";

    public static string PhotoCategoryPage(
        string version,
        int catId,
        int page,
        int pageSize,
        string? sizeFilter = null) =>
        $"{PhotoCategoryPageSegment}:v{version}:{catId}:{page}:{pageSize}:size={sizeFilter ?? string.Empty}";

    public static string OnThisDay(string version, DateOnly date, int count) =>
        $"{Prefix}:history:on-this-day:v{version}:{date:yyyyMMdd}:{count}";

    public static string AroundThisDay(string version, DateOnly date, int dayWindow, int count) =>
        $"{Prefix}:history:around-this-day:v{version}:{date:yyyyMMdd}:{dayWindow}:{count}";

    public static string AllPublishedHistory(string version) =>
        $"{HistoryAllPublishedSegment}:v{version}";

    public static string ForumRecentThreads(int count) =>
        $"{ForumRecentThreadsSegment}:{count}";

    public static string FanPerformancePage(string version, int page, int pageSize) =>
        $"{FanPerformancePageSegment}:v{version}:{page}:{pageSize}";

    public static string FanPerformanceVisibleCount(string version) =>
        $"{FanPerformanceCountSegment}:v{version}";

    public static string FanPerformanceById(string version, int id) =>
        $"{FanPerformanceByIdSegment}:v{version}:{id}";
}
