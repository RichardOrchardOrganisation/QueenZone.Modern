using QueenZone.Data.Migrations;

namespace QueenZone.Web.Tests;

public sealed class PublicReadPerformanceSqlTests
{
    // Hand-authored SQL is a contract: keep the deployed migration and reviewable script identical.
    [Fact]
    public void TopicPostsProcedure_MatchesDocumentation_AndPagesBeforeReadingBodies()
    {
        var script = File.ReadAllText(RepoPaths.Combine("docs", "sql", "006-modern-forum-read-path.sql"));
        Assert.Contains(OptimizePublicReadQueries.TopicPostsProcedureSql.Replace("\r\n", "\n"), script.Replace("\r\n", "\n"), StringComparison.Ordinal);
        var sql = OptimizePublicReadQueries.TopicPostsProcedureSql;
        Assert.True(sql.IndexOf("OFFSET @Offset", StringComparison.Ordinal) < sql.IndexOf("p.BodyHtml AS", StringComparison.Ordinal));
        Assert.Contains("INNER JOIN dbo.ModernForumPost p ON p.Id = page.Id", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ForumIndexes_MatchDocumentation_AndKeepLargeBodiesOut()
    {
        var script = File.ReadAllText(RepoPaths.Combine("docs", "sql", "012-public-read-performance-indexes.sql"));
        Assert.Contains(OptimizePublicReadQueries.ForumIndexesSql.Replace("\r\n", "\n"), script.Replace("\r\n", "\n"), StringComparison.Ordinal);
        Assert.DoesNotContain("BodyHtml", OptimizePublicReadQueries.ForumIndexesSql, StringComparison.Ordinal);
        Assert.Contains("INCLUDE (PostedAt, AuthorDisplayName, IsHidden)", OptimizePublicReadQueries.ForumIndexesSql, StringComparison.Ordinal);
    }
}
