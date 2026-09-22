using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class FreddieTributeSqlTests
{
    [Fact]
    public void ProductionSql_PicksIdsWithoutNewIdAndKeepsTrimmedThoughtPredicate()
    {
        Assert.DoesNotContain("NEWID()", FreddieTributeSql.PageSql, StringComparison.Ordinal);
        Assert.DoesNotContain("NEWID()", FreddieTributeSql.CountSql, StringComparison.Ordinal);
        Assert.DoesNotContain("NEWID()", FreddieTributeSql.IdBoundsSql, StringComparison.Ordinal);
        Assert.DoesNotContain("NEWID()", FreddieTributeSql.IdSeekAtOrAfterSql, StringComparison.Ordinal);
        Assert.DoesNotContain("NEWID()", FreddieTributeSql.IdSeekBeforeSql, StringComparison.Ordinal);
        Assert.DoesNotContain("NEWID()", FreddieTributeSql.ByIdSql, StringComparison.Ordinal);
        Assert.Contains("MIN(ID) AS MinId", FreddieTributeSql.IdBoundsSql, StringComparison.Ordinal);
        Assert.Contains("MAX(ID) AS MaxId", FreddieTributeSql.IdBoundsSql, StringComparison.Ordinal);
        Assert.Contains("ID >= {0}", FreddieTributeSql.IdSeekAtOrAfterSql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY ID", FreddieTributeSql.IdSeekAtOrAfterSql, StringComparison.Ordinal);
        Assert.Contains("ID < {0}", FreddieTributeSql.IdSeekBeforeSql, StringComparison.Ordinal);
        Assert.Contains("WHERE ID = {0}", FreddieTributeSql.ByIdSql, StringComparison.Ordinal);
        Assert.Contains(
            "NULLIF(LTRIM(RTRIM(ISNULL(Thought, ''))), '') IS NOT NULL",
            FreddieTributeSql.CountSql,
            StringComparison.Ordinal);
        Assert.Contains(
            "NULLIF(LTRIM(RTRIM(ISNULL(Thought, ''))), '') IS NOT NULL",
            FreddieTributeSql.PageSql,
            StringComparison.Ordinal);
    }
}
