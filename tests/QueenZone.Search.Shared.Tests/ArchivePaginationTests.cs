using QueenZone.Routing;

namespace QueenZone.Search.Shared.Tests;

public sealed class ArchivePaginationTests
{
    [Fact]
    public void BuildViewModel_OmitsNavigationForSinglePage()
    {
        Assert.Null(ArchivePagination.BuildViewModel("test", 1, 1, page => $"/p/{page}"));
    }

    [Fact]
    public void BuildViewModel_ListsEveryPageWhenArchiveFitsInTwoEdgeClusters()
    {
        var nav = ArchivePagination.BuildViewModel("test", 1, 8, page => $"/p/{page}");

        Assert.NotNull(nav);
        Assert.Equal(
            new int?[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            nav.Pages.Select(p => p.PageNumber).ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(12)]
    [InlineData(15)]
    public void BuildViewModel_NearEitherEnd_ShowsFirstAndLastClusters(int currentPage)
    {
        var nav = ArchivePagination.BuildViewModel("test", currentPage, 15, page => $"/p/{page}");

        Assert.NotNull(nav);
        Assert.Equal(
            new int?[] { 1, 2, 3, 4, null, 12, 13, 14, 15 },
            nav.Pages.Select(p => p.PageNumber).ToArray());
        Assert.Contains(nav.Pages, p => p.PageNumber == currentPage && p.IsCurrent);
    }

    [Fact]
    public void BuildViewModel_InMiddle_ShowsFirstCurrentWindowAndLast()
    {
        var nav = ArchivePagination.BuildViewModel("test", 8, 15, page => $"/p/{page}");

        Assert.NotNull(nav);
        Assert.Equal(
            new int?[] { 1, null, 7, 8, 9, null, 15 },
            nav.Pages.Select(p => p.PageNumber).ToArray());
        Assert.Contains(nav.Pages, p => p.PageNumber == 8 && p.IsCurrent && p.Href is null);
        Assert.Equal("/p/7", nav.PreviousHref);
        Assert.Equal("/p/9", nav.NextHref);
    }

    [Fact]
    public void GetTotalPages_NegativeCount_ReturnsZero()
    {
        Assert.Equal(0, ArchivePagination.GetTotalPages(-1, 1));
    }

    [Theory]
    [InlineData(5, 3, 0, 1, 5)]
    [InlineData(5, 0, 0, 1, 0)]
    [InlineData(3, 0, 25, 2, 2)]
    public void ResolveTotalPages_CountAndEmptyStates_ReturnExpectedTotal(
        int currentPage, int itemCount, int publishedCount, int totalPages, int expected)
    {
        Assert.Equal(expected, ArchivePagination.ResolveTotalPages(currentPage, itemCount, publishedCount, totalPages, 20));
    }

    [Fact]
    public void BuildViewModel_LastPage_HasNoNextLink()
    {
        var nav = ArchivePagination.BuildViewModel("test", 15, 15, page => $"/p/{page}");
        Assert.NotNull(nav);
        Assert.Null(nav.NextHref);
        Assert.Equal("/p/14", nav.PreviousHref);
    }

    [Fact]
    public void BuildViewModel_BeforeLastCluster_ShowsMiddleWindow()
    {
        var nav = ArchivePagination.BuildViewModel("test", 11, 15, page => $"/p/{page}");
        Assert.NotNull(nav);
        Assert.Equal(new int?[] { 1, null, 10, 11, 12, null, 15 }, nav.Pages.Select(p => p.PageNumber).ToArray());
    }
}
