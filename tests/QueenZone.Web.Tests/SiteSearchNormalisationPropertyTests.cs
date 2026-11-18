using CsCheck;
using QueenZone.Data;
using QueenZone.TestUtilities;
using QueenZone.Web.Search;

namespace QueenZone.Web.Tests;

public sealed class SiteSearchNormalisationPropertyTests
{
    private static readonly Gen<string?> Queries = Gen.OneOf(
        Gen.OneOf<string?>(null, "", " ", "\t\r\n", "Q", " q ", "東京", "Queen", " QUEEN live ",
            "Queen  live", "Queen\t\nLive", "\u00a0Queen\u00a0", new string('Q', 2_000)),
        from word in Gen.OneOf("Queen", "LIVE", "1975", "東京", "q", "")
        from gap in Gen.OneOf(" ", "  ", "\t", "\r\n", " \t\n ", "\u00a0\u00a0")
        from count in Gen.Int[0, 100]
        select (string?)($"{gap}{string.Join(gap, Enumerable.Repeat(word, count))}{gap}"));

    private static readonly Gen<int> Numbers = Gen.OneOf(
        Gen.Int, Gen.OneOf(int.MinValue, -1, 0, 1, 2, 10, 11, 99, 100, 101, int.MaxValue));

    [Fact]
    public void Normalize_Issue2532ShrunkCase_CollapsesWhitespace()
    {
        // Replay seed: ePrTibw9i1581884.
        var key = SiteSearchCacheKey.Normalize(" Queen \r\n Tour\t", null, 1, 20);
        Assert.Equal("queen tour", key.Query);
    }

    [Fact]
    public void Normalize_IsIdempotentAndRespectsLimits()
    {
        var cases = from query in Queries
                    from contentType in Gen.OneOf<string?>(null, "", " \t", "NEWS", " articles ")
                    from page in Numbers
                    from size in Numbers
                    select (query, contentType, page, size);
        PropertyRun.Run(nameof(Normalize_IsIdempotentAndRespectsLimits), cases, input =>
        {
            // Null is intentional: the runtime contract accepts it despite the parameter annotation.
            var key = SiteSearchCacheKey.Normalize(input.query!, input.contentType, input.page, input.size);
            Assert.Equal(key, SiteSearchCacheKey.Normalize(key.Query, key.ContentType, key.Page, key.PageSize));
            Assert.Equal(input.page < 1 ? 1 : input.page, key.Page);
            var expectedSize = input.size < 1 ? 1 : input.size > SiteSearchLimits.MaxPageSize
                ? SiteSearchLimits.MaxPageSize : input.size;
            Assert.Equal(expectedSize, key.PageSize);
            Assert.Equal(key.Page > SiteSearchLimits.MaxPage, SiteSearchLimits.IsBeyondMaxPage(key.Page));
            Assert.Equal(key.Page > SiteSearchLimits.MaxPage, SiteSearchLimits.IsBeyondMaxPage(input.page));
            Assert.Equal(key.Page, SiteSearchLimits.NormalizePage(input.page));
            Assert.Equal(expectedSize, SiteSearchLimits.NormalizePageSize(input.size));
        });
    }

    [Fact]
    public void Normalize_HasNoEdgeOrConsecutiveWhitespace()
    {
        var cases = from query in Queries
                    from first in Gen.OneOf("Queen", "LIVE", "1975", "東京")
                    from second in Gen.OneOf("music", "Tour", "album")
                    from gap in Gen.OneOf("  ", "\t\n", " \r\n ", "\u00a0\u00a0")
                    select (query, repeatedWhitespace: $" {first}{gap}{second}\t");
        PropertyRun.Run(nameof(Normalize_HasNoEdgeOrConsecutiveWhitespace), cases, input =>
        {
            foreach (var query in new[] { input.query, input.repeatedWhitespace })
            {
                var key = SiteSearchCacheKey.Normalize(query!, null, 1, 20);
                Assert.DoesNotMatch(@"\A\s|\s\z|\s{2}", key.Query);
            }
        });
    }

    [Fact]
    public void ShortOrBlankQueries_HaveNoSearch()
    {
        PropertyRun.Run(nameof(ShortOrBlankQueries_HaveNoSearch), Queries, query =>
        {
            var characters = (query ?? "").AsEnumerable().SkipWhile(char.IsWhiteSpace)
                .Reverse().SkipWhile(char.IsWhiteSpace).Count();
            var expected = characters < SiteSearchLimits.MinQueryLength;
            Assert.Equal(expected, SiteSearchLimits.IsBelowMinimumLength(query));
            if (expected)
            {
                var key = SiteSearchCacheKey.Normalize(query!, null, 1, 20);
                Assert.True(SiteSearchLimits.IsBelowMinimumLength(key.Query));
            }
        });
    }
}
