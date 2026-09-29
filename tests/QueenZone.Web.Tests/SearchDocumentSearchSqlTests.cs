using QueenZone.Data;

namespace QueenZone.Web.Tests;

// The search SQL is a hand-written script that only runs on SQL Server, which these tests can't reach.
// Its text is therefore the contract: each test slices one branch of the script and pins its shape.
public sealed class SearchDocumentSearchSqlTests
{
    [Fact]
    public void Rank_cap_matches_the_sql_source_of_truth()
    {
        Assert.Equal(1000, SiteSearchLimits.MaxRankedMatches);
        Assert.Equal(5000, SiteSearchLimits.TypedMatchScanLimit);
        Assert.Contains(
            "@RankLimit      INT = 1000",
            ReadSqlSourceOfTruth(),
            StringComparison.Ordinal);
    }
