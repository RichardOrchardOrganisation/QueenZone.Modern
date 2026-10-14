using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

public sealed class ForumAuthorSummarySqlServerTests : IAsyncLifetime
{
    private readonly ModernForumSqlServerDatabase database = new();
    private QueenZoneDbContext context = null!;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        context = database.CreateContext();
        await database.ExecuteAsync("""
            ALTER TABLE dbo.ModernForumPost ALTER COLUMN AuthorDisplayName
                nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL;
            ALTER TABLE dbo.ModernForumThread ALTER COLUMN StartedByDisplayName
                nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL;
            """);
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Summary_preserves_author_identity_counts_and_production_collation(bool hidden)
    {
        var memberId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await database.SeedCategoryAsync(1, 1, "General", 1);
        await SeedThreadAsync(10, "Forum Fan", memberId, hidden);
        await SeedThreadAsync(11, "Former Name", memberId, hidden);
        await SeedThreadAsync(12, "forum fan", null, hidden);
        await SeedThreadAsync(13, "Forum Fan", otherId, false);
        await SeedThreadAsync(14, "Forum Fán", null, false);
        await database.SeedPostAsync(100, 100, 10, 10, 1, "Forum Fan", "Linked reply",
            authorMemberId: memberId, isHidden: hidden);
        await database.SeedPostAsync(101, 101, 10, 10, 1, "FORUM FAN", "Unlinked reply",
            isHidden: hidden);

        var repository = new EfForumWriteRepository(context);
        var summary = await repository.GetAuthorForumContentSummaryAsync(memberId, "  Forum Fan  ");
        Assert.Equal(5, summary.PostCount);
        Assert.Equal(3, summary.ThreadCount);
        Assert.Equal(hidden, summary.IsHidden);

        var unlinked = await repository.GetAuthorForumContentSummaryAsync(null, "Forum Fan");
        Assert.Equal(2, unlinked.PostCount);
        Assert.Equal(1, unlinked.ThreadCount);
        Assert.Equal(hidden, unlinked.IsHidden);

        var missing = await repository.GetAuthorForumContentSummaryAsync(Guid.NewGuid(), "Missing");
        Assert.Equal(0, missing.PostCount);
        Assert.Equal(0, missing.ThreadCount);
        Assert.False(missing.IsHidden);
    }

    [Fact]
    public async Task Summary_count_uses_author_seeks_on_a_representative_scratch_archive()
    {
        var memberId = Guid.NewGuid();
        const string name = "Synthetic Target";
        await database.SeedCategoryAsync(1, 1, "General", 1);
        await SeedThreadAsync(10, name, memberId, false);
        await SeedThreadAsync(11, "Former Name", memberId, true);
        await SeedThreadAsync(12, "synthetic target", null, true);
        await database.ExecuteAsync("""
            INSERT dbo.ModernForumPost
                (LegacyPostId, LegacyThreadTopicId, ThreadId, LegacyForumId,
                 AuthorDisplayName, BodyHtml, LegacyDiscography, AttachCount)
            SELECT TOP (100000) 1000 + ROW_NUMBER() OVER (ORDER BY (SELECT NULL)),
                10, 10, 1, CONCAT(N'Unrelated ', ROW_NUMBER() OVER (ORDER BY (SELECT NULL))),
                REPLICATE(N'x', 128), 0, 0
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            CREATE INDEX IX_AuthorSummary_Linked ON dbo.ModernForumPost (AuthorMemberId, PostedAt DESC)
                INCLUDE (ThreadId, AuthorDisplayName, IsHidden) WHERE AuthorMemberId IS NOT NULL;
            CREATE INDEX IX_AuthorSummary_Name ON dbo.ModernForumPost (AuthorDisplayName, PostedAt);
            UPDATE STATISTICS dbo.ModernForumPost WITH FULLSCAN;
            """);

        var repository = new EfForumWriteRepository(context);
        Assert.Equal(3, await repository.SummaryAuthorPosts(memberId, name).CountAsync());
        var original = await MeasureCountAsync("""
            SELECT COUNT(*) FROM dbo.ModernForumPost
            WHERE AuthorMemberId = @member OR (AuthorMemberId IS NULL AND AuthorDisplayName = @name)
            """, memberId, name);
        var repaired = await MeasureCountAsync("""
            SELECT COUNT(*) FROM (
                SELECT Id FROM dbo.ModernForumPost WHERE AuthorMemberId = @member
                UNION ALL
                SELECT Id FROM dbo.ModernForumPost WHERE AuthorMemberId IS NULL AND AuthorDisplayName = @name
            ) matches
            """, memberId, name);
        Assert.Equal(3, original.Count);
        Assert.Equal(original.Count, repaired.Count);
        var plan = XDocument.Parse(repaired.ActualPlan);
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        var seekIndexes = plan.Descendants(ns + "RelOp")
            .Where(node => (string?)node.Attribute("PhysicalOp") == "Index Seek")
            .SelectMany(node => node.Descendants(ns + "Object"))
            .Select(node => (string?)node.Attribute("Index"))
            .ToArray();
        Assert.Contains("[IX_AuthorSummary_Linked]", seekIndexes);
        Assert.Contains("[IX_AuthorSummary_Name]", seekIndexes);

        // CI uploads TestResults. Retain plans/IO/timing without a fragile elapsed-time gate.
        var root = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? AppContext.BaseDirectory;
        var directory = Path.Combine(root, "TestResults", "author-summary-performance");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "comparison.json"),
            JsonSerializer.Serialize(new { SyntheticUnrelatedRows = 100000, Original = original, Repaired = repaired }));
    }

    private async Task<CountDiagnostic> MeasureCountAsync(string sql, Guid memberId, string name)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var messages = new List<string>();
        connection.InfoMessage += (_, args) => messages.Add(args.Message);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET STATISTICS IO ON; SET STATISTICS XML ON; {sql}";
        command.Parameters.Add(new SqlParameter("@member", System.Data.SqlDbType.UniqueIdentifier) { Value = memberId });
        command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 100) { Value = name });
        var timer = Stopwatch.StartNew();
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var count = reader.GetInt32(0);
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        var plan = reader.GetString(0);
        timer.Stop();
        return new CountDiagnostic(count, timer.Elapsed.TotalMilliseconds, messages.ToArray(), plan);
    }

    private sealed record CountDiagnostic(int Count, double ElapsedMs, string[] StatisticsIo, string ActualPlan);

    private async Task SeedThreadAsync(long id, string name, Guid? memberId, bool hidden)
    {
        await database.SeedThreadAsync(id, (int)id, 1, 1, $"Thread {id}", name, isHidden: hidden);
        await database.SeedPostAsync(id, (int)id, (int)id, id, 1, name, "Starter",
            authorMemberId: memberId, isHidden: hidden);
    }
}
