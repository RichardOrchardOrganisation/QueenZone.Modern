using QueenZone.Data;
using QueenZone.Routing;

namespace QueenZone.Search.Shared.Tests;

public sealed class ForumRoutesTests
{
    [Fact]
    public void GetCategoryPath_BuildsSlugFromCategoryName()
    {
        var category = new ForumCategorySummary(
            1,
            "Queen - Serious Discussion",
            "SERIOUS discussion on the greatest band in the land",
            344_828,
            new DateTime(2017, 7, 15, 20, 30, 0, DateTimeKind.Utc),
            "In response to who likes Bowie",
            "/forum/1/queen-serious-discussion");

        Assert.Equal("/forum/1/queen-serious-discussion", ForumRoutes.GetCategoryPath(category));
    }

    [Theory]
    [InlineData(500, "500")]
    [InlineData(1_500, "1.5k+")]
    [InlineData(1_250_000, "1.3M+")]
    public void FormatCount_UsesCompactSuffixesForLargeValues(long value, string expected) =>
        Assert.Equal(expected, ForumRoutes.FormatCount(value));

    [Fact]
    public void GetCategoryCanonicalPath_UsesPageSegmentAfterFirstPage()
    {
        var category = new ForumCategorySummary(1, "Queen - Serious Discussion", null, 0, null, null, "/forum/1/queen-serious-discussion");

        Assert.Equal("/forum/1/queen-serious-discussion", ForumRoutes.GetCategoryCanonicalPath(category));
        Assert.Equal("/forum/1/queen-serious-discussion/page/3", ForumRoutes.GetCategoryCanonicalPath(category, 3));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 1)]
    [InlineData(26, 2)]
    [InlineData(13075, 523)]
    public void GetTopicsTotalPages_MatchesLegacyStoredProcedurePaging(int totalCount, int expectedPages) =>
        Assert.Equal(expectedPages, ForumRoutes.GetTopicsTotalPages(totalCount));

    [Fact]
    public void GetTopicCanonicalPath_UsesPageSegmentAfterFirstPage()
    {
        Assert.Equal("/forum/topic/1002/ranking-every-studio-album", ForumRoutes.GetTopicCanonicalPath(1002, "Ranking every studio album"));
        Assert.Equal("/forum/topic/1002/ranking-every-studio-album/page/3", ForumRoutes.GetTopicCanonicalPath(1002, "Ranking every studio album", 3));
    }

    [Theory]
    [InlineData(15, 1)]
    [InlineData(16, 2)]
    [InlineData(31, 3)]
    public void GetPostsTotalPages_UsesFifteenPostPageSize(int totalCount, int expectedPages) =>
        Assert.Equal(expectedPages, ForumRoutes.GetPostsTotalPages(totalCount));

    [Theory]
    [InlineData(1002, 0, "/forum/topic/1002/ranking-every-studio-album")]
    [InlineData(1101, 1, "/forum/topic/1002/ranking-every-studio-album#post-1101")]
    [InlineData(1200, 14, "/forum/topic/1002/ranking-every-studio-album#post-1200")]
    [InlineData(1201, 15, "/forum/topic/1002/ranking-every-studio-album/page/2#post-1201")]
    public void GetLegacyPostTargetPath_PagesAndAnchorsReplies(int postId, int postIndex, string expected) =>
        Assert.Equal(
            expected,
            ForumRoutes.GetLegacyPostTargetPath(new ForumLegacyPostLocation(1002, "Ranking every studio album", postId, postIndex)));

    [Fact]
    public void GetLegacyPostPath_UsesGotoRoute() =>
        Assert.Equal("/forum/goto/413200", ForumRoutes.GetLegacyPostPath(413200));
}
