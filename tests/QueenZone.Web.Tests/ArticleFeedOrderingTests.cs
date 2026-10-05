using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class ArticleFeedOrderingTests
{
    [Fact]
    public void Sort_orders_by_date_descending()
    {
        var newer = ArticleFeedKey.Archive(1, new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var older = ArticleFeedKey.Community(Guid.NewGuid(), new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal([newer, older], ArticleFeedOrdering.Sort([older, newer]));
    }

    [Fact]
    public void Sort_puts_community_before_archive_on_equal_timestamps()
    {
        var date = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var community = ArticleFeedKey.Community(Guid.Parse("00000000-0000-0000-0000-000000000002"), date);
        var archive = ArticleFeedKey.Archive(10, date);

        Assert.Equal([community, archive], ArticleFeedOrdering.Sort([archive, community]));
    }

    [Fact]
    public void Sort_uses_id_descending_within_each_source()
    {
        var date = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var communityLow = ArticleFeedKey.Community(Guid.Parse("00000000-0000-0000-0000-000000000001"), date);
        var communityHigh = ArticleFeedKey.Community(Guid.Parse("00000000-0000-0000-0000-00000000000A"), date);
        var archiveLow = ArticleFeedKey.Archive(2, date);
        var archiveHigh = ArticleFeedKey.Archive(9, date);

        Assert.Equal(
            [communityHigh, communityLow, archiveHigh, archiveLow],
            ArticleFeedOrdering.Sort([archiveLow, communityLow, archiveHigh, communityHigh]));
    }
}
