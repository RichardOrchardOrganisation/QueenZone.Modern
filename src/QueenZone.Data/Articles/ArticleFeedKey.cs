namespace QueenZone.Data;

public enum ArticleFeedSource
{
    Community = 0,
    Archive = 1,
}

/// <summary>
/// Lightweight published-article identity used to merge community and archive
/// feeds in process. Ids stay typed because the two sources do not share a key.
/// </summary>
public readonly record struct ArticleFeedKey(
    ArticleFeedSource Source,
    Guid CommunityId,
    int ArchiveId,
    DateTime PublishedAtUtc)
{
    public static ArticleFeedKey Community(Guid id, DateTime publishedAtUtc) =>
        new(ArticleFeedSource.Community, id, 0, publishedAtUtc);

    public static ArticleFeedKey Archive(int id, DateTime publishedAtUtc) =>
        new(ArticleFeedSource.Archive, Guid.Empty, id, publishedAtUtc);
}
