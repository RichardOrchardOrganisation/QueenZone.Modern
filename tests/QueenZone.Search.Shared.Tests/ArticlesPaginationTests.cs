using QueenZone.Routing;

namespace QueenZone.Search.Shared.Tests;

public sealed class ArticlesPaginationTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(20, 1)]
    [InlineData(21, 2)]
    public void GetArchiveTotalPages_CalculatesExpectedPageCount(int publishedCount, int expectedTotalPages)
    {
        Assert.Equal(expectedTotalPages, ArticlesRoutes.GetArchiveTotalPages(publishedCount));
    }

    [Theory]
    [InlineData(1, "/articles")]
    [InlineData(2, "/articles/page/2")]
    public void GetArchiveCanonicalPath_UsesArticlesForFirstPage(int page, string expectedPath)
    {
        Assert.Equal(expectedPath, ArticlesRoutes.GetArchiveCanonicalPath(page));
    }

    [Fact]
    public void GetArchivePaginationViewModel_IncludesPreviousNextAndPageLinks()
    {
        var nav = ArticlesRoutes.GetArchivePaginationViewModel(2, 4);

        Assert.NotNull(nav);
        Assert.Equal(2, nav.CurrentPage);
        Assert.Equal(4, nav.TotalPages);
        Assert.Equal("/articles", nav.PreviousHref);
        Assert.Equal("/articles/page/3", nav.NextHref);
    }

    [Theory]
    [InlineData("queen", 1, "/articles?tag=queen")]
    [InlineData("queen", 2, "/articles?tag=queen&page=2")]
    public void GetTaggedListPath_UsesQueryPaging(string tag, int page, string expectedPath)
    {
        Assert.Equal(expectedPath, ArticlesRoutes.GetTaggedListPath(tag, page));
    }
}
