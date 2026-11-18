using CsCheck;
using QueenZone.Routing;
using QueenZone.TestUtilities;

namespace QueenZone.Search.Shared.Tests;

public sealed class ArchivePaginationPropertyTests
{
    private static readonly Gen<int> Counts = Gen.OneOf(Gen.Int[0, int.MaxValue], Gen.Int[0, 1_000], Gen.OneOf(0, 1, int.MaxValue));
    private static readonly Gen<int> Sizes = Gen.OneOf(Gen.Int[1, int.MaxValue], Gen.Int[1, 100], Gen.OneOf(1, 2, int.MaxValue));

    [Fact]
    public void GetTotalPages_MatchesCeilingAndTilesCount()
    {
        var cases = from count in Counts
                    from size in Sizes
                    from fraction in Gen.Int[0, int.MaxValue]
                    select (count, size, fraction);
        PropertyRun.Run(nameof(GetTotalPages_MatchesCeilingAndTilesCount), cases, input =>
        {
            var (count, size, fraction) = input;
            var expected = (int)(((long)count + size - 1) / size);
            var actual = ArchivePagination.GetTotalPages(count, size);
            Assert.Equal(expected, actual);
            if (actual == 0)
            {
                Assert.Equal(0, count);
                return;
            }

            var page = 1 + fraction % actual;
            var start = (long)(page - 1) * size;
            var end = Math.Min((long)page * size, count);
            Assert.InRange(end - start, 1, size);
            var lastStart = (long)(actual - 1) * size;
            var lastEnd = Math.Min((long)actual * size, count);
            Assert.InRange(lastEnd - lastStart, 1, size);
            Assert.Equal(count, lastEnd);
            Assert.Equal(count, lastStart + (lastEnd - lastStart));
            if (page < actual)
            {
                Assert.Equal(end, (long)((page + 1) - 1) * size);
            }
        }, PropertyRun.CheapIterations);
    }

    [Fact]
    public void ResolveTotalPages_KeepsKnownTotalOnEmptyOutOfRangePages()
    {
        var cases = from count in Counts
                    from size in Sizes
                    select (count, size);
        PropertyRun.Run(nameof(ResolveTotalPages_KeepsKnownTotalOnEmptyOutOfRangePages), cases, input =>
        {
            var (count, size) = input;
            var total = (int)(((long)count + size - 1) / size);
            if (count == 0)
            {
                Assert.Equal(0, ArchivePagination.ResolveTotalPages(1, 0, count, total, size));
                return;
            }

            // Archive requests use positive page numbers; use long for the conceptual page beyond int.MaxValue.
            foreach (var page in new[] { 1L, (long)total, (long)total + 1 })
            {
                var items = (int)Math.Max(0, Math.Min(size, count - (page - 1) * size));
                if (page <= int.MaxValue && !(items == size && total <= page))
                {
                    Assert.Equal(total, ArchivePagination.ResolveTotalPages((int)page, items, count, total, size));
                }
                if (page > total)
                {
                    Assert.Equal(0, items);
                }
            }
        }, PropertyRun.CheapIterations);
    }

    [Fact]
    public void ResolveTotalPages_MatchesAvailableEvidence()
    {
        // Archive navigation is exercised over a practical positive range; negative pages are not archive requests.
        var cases = from page in Gen.Int[1, 10_000]
                    from size in Gen.Int[1, 100]
                    from items in Gen.Int[0, size]
                    from total in Gen.Int[0, 10_000]
                    from published in Counts
                    select (page, size, items, total, published);
        PropertyRun.Run(nameof(ResolveTotalPages_MatchesAvailableEvidence), cases, input =>
        {
            var (page, size, items, total, published) = input;
            var evidence = new[] { total, items > 0 ? page : 0, items == size ? page + 1 : 0 };
            var expected = items == size && total <= page ? evidence.Max()
                : published > 0 ? total
                : items == 0 ? 0 : evidence.Max();
            Assert.Equal(expected, ArchivePagination.ResolveTotalPages(page, items, published, total, size));
        }, PropertyRun.CheapIterations);
    }

    [Fact]
    public void BuildViewModel_BoundsLinksAndOmitsTerminalNavigation()
    {
        // Navigation uses practical archive sizes and positive pages, including pages just beyond the last.
        var cases = from total in Gen.Int[0, 10_000]
                    from page in Gen.OneOf(Gen.Int[1, total + 2], Gen.OneOf(1, Math.Max(1, total), total + 1))
                    select (total, page);
        PropertyRun.Run(nameof(BuildViewModel_BoundsLinksAndOmitsTerminalNavigation), cases, input =>
        {
            var (total, page) = input;
            var nav = ArchivePagination.BuildViewModel("archive", page, total, p => $"/page/{p}");
            if (total <= 1)
            {
                Assert.Null(nav);
                return;
            }

            Assert.NotNull(nav);
            Assert.Equal(total, nav.TotalPages);
            Assert.Equal(page > 1 ? $"/page/{page - 1}" : null, nav.PreviousHref);
            Assert.Equal(page < total ? $"/page/{page + 1}" : null, nav.NextHref);
            Assert.All(nav.Pages.Where(p => p.PageNumber.HasValue), p => Assert.InRange(p.PageNumber!.Value, 1, total));
        }, PropertyRun.CheapIterations);
    }
}
