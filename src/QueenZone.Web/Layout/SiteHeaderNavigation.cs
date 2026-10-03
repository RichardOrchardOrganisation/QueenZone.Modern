namespace QueenZone.Web;

public sealed record SiteHeaderNavItem(string Title, string Href, string Description, string Tag);

public sealed record SiteHeaderNavGroup(string Id, string Label, string Eyebrow, string Accent, IReadOnlyList<SiteHeaderNavItem> Items);

public static class SiteHeaderNavigation
{
    private static SiteHeaderNavGroup Band { get; } = new("band", "The Band", "About Queen", "purple",
    [
        new("Biography", "/biography", "The story of the band, member by member", ""),
        new("Discography", "/discography", "The core albums", ""),
        new("Rare Discography", "/discography/rare-discography", "John S Stuart's rare recordings and analysis posts", ""),
        new("Timeline", "/timeline", "Five decades, year by year", ""),
        new("Trivia", "/trivia", "Random Queen facts from the archive", ""),
    ]);

    private static SiteHeaderNavGroup Archive { get; } = new("archive", "Archive", "The Publication", "blue",
    [
        new("News", "/news", "4,000+ articles from the original archive", ""),
        new("Articles", "/articles", "Community articles and long-form features from the archive", ""),
        new("Photography", "/photography", "Thousands of restored images", ""),
        new("Links", "/links", "Checked Queen-related websites from the archive", ""),
    ]);

    private static SiteHeaderNavGroup Community { get; } = new("community", "Community", "The Fans", "burgundy",
    [
        new("Forum", "/forum", "100,000+ posts from the membership", ""),
        new("Fan Performances", "/fan-performances", "Covers, tributes and live sets", ""),
        new("Freddie Tribute", "/freddie-mercury-tribute", "Visitor messages remembering Freddie Mercury", ""),
    ]);
    public static IReadOnlyList<SiteHeaderNavGroup> Groups { get; } = [Band, Archive, Community];
}
