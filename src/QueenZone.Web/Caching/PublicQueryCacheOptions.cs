namespace QueenZone.Web;

public sealed class PublicQueryCacheOptions
{
    public const string SectionName = "PublicQueryCache";

    public TimeSpan NewsCacheDuration { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan ArticleCountCacheDuration { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan ForumStatsCacheDuration { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan OnThisDayCacheDuration { get; init; } = TimeSpan.FromHours(12);

    /// <summary>Photography archive changes rarely; default is generous.</summary>
    public TimeSpan PhotoCacheDuration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Fan-performance archive changes rarely; default matches photography.</summary>
    public TimeSpan FanPerformanceCacheDuration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Short-lived: the mobile home screen's live-activity strip is meant to feel current.</summary>
    public TimeSpan LiveActivityCacheDuration { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Stable catalog slices (published quotes/trivia pools, biography chapters, discography albums).
    /// </summary>
    public TimeSpan CatalogCacheDuration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Featured Freddie tribute id and the four tribute-page photo ids.</summary>
    public TimeSpan FreddieSampleCacheDuration { get; init; } = TimeSpan.FromMinutes(5);
}
