namespace QueenZone.Data;

/// <summary>
/// Total order for the merged articles index: date DESC, community before archive,
/// then id DESC within the source.
/// </summary>
public static class ArticleFeedOrdering
{
    public static readonly IComparer<ArticleFeedKey> Comparer = new KeyComparer();

    public static IReadOnlyList<ArticleFeedKey> Sort(IEnumerable<ArticleFeedKey> keys) =>
        keys.Order(Comparer).ToList();

    private sealed class KeyComparer : IComparer<ArticleFeedKey>
    {
        public int Compare(ArticleFeedKey x, ArticleFeedKey y)
        {
            var date = y.PublishedAtUtc.CompareTo(x.PublishedAtUtc);
            if (date != 0)
            {
                return date;
            }

            var source = x.Source.CompareTo(y.Source);
            if (source != 0)
            {
                return source;
            }

            return x.Source == ArticleFeedSource.Archive
                ? y.ArchiveId.CompareTo(x.ArchiveId)
                : y.CommunityId.CompareTo(x.CommunityId);
        }
    }
}
