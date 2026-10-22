using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using QueenZone.Data;
using Xunit.Abstractions;

namespace QueenZone.SqlServerTests;

public sealed class ForumPostPositionCountSqlServerTests(ITestOutputHelper output) : IAsyncLifetime
{
    private readonly ModernForumSqlServerDatabase database = new();
    private QueenZoneDbContext context = null!;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        context = database.CreateContext();
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task Post_position_uses_thread_index_seek_with_fewer_logical_reads()
    {
        const int postId = 45000;
        await database.SeedCategoryAsync(1, 1, "General", 1);
        for (var threadId = 10; threadId <= 25; threadId++)
        {
            await database.SeedThreadAsync(threadId, threadId, 1, 1, $"Thread {threadId}", "Synthetic Author");
        }

        await database.ExecuteAsync("""
            ;WITH Numbers AS (
                SELECT TOP (200000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                FROM sys.all_objects a CROSS JOIN sys.all_objects b
            )
            INSERT dbo.ModernForumPost
                (LegacyPostId, LegacyThreadTopicId, ThreadId, LegacyForumId,
                 AuthorDisplayName, BodyHtml, LegacyDiscography, AttachCount)
            SELECT n,
                CASE WHEN n <= 50000 THEN 10 ELSE 11 + (n - 50001) / 10000 END,
                CASE WHEN n <= 50000 THEN 10 ELSE 11 + (n - 50001) / 10000 END,
                1, N'Synthetic Author', REPLICATE(N'x', 512), 0, 0
            FROM Numbers;

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.ModernForumPost') AND name = N'IX_ModernForumPost_Thread_Posted')
                CREATE INDEX IX_ModernForumPost_Thread_Posted
                    ON dbo.ModernForumPost (ThreadId, LegacyPostId)
                    INCLUDE (PostedAt, AuthorDisplayName, IsHidden);
            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.ModernForumPost') AND name = N'IX_ModernForumPost_VisibleDiscussion')
                CREATE INDEX IX_ModernForumPost_VisibleDiscussion
                    ON dbo.ModernForumPost (LegacyThreadTopicId, PostedAt, LegacyPostId)
                    WHERE IsHidden = 0;
            UPDATE STATISTICS dbo.ModernForumPost WITH FULLSCAN;
            """);

        var post = await new EfForumWriteRepository(context).GetPostAsync(postId);
        Assert.NotNull(post);
        Assert.Equal(45000, post.PositionInThread);

        var before = await MeasureCountAsync("""
            SELECT COUNT(*) FROM dbo.ModernForumPost
            WHERE LegacyThreadTopicId = @topic AND LegacyPostId <= @postId;
            """, postId);
        var after = await MeasureCountAsync("""
            DECLARE @thread bigint;
            SELECT @thread = ThreadId FROM dbo.ModernForumPost WHERE LegacyPostId = @postId;
            SELECT COUNT(*) FROM dbo.ModernForumPost
            WHERE ThreadId = @thread AND LegacyPostId <= @postId;
            """, postId);

        output.WriteLine($"Post-position logical reads: before={before.LogicalReads}, after={after.LogicalReads}");
        var root = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? AppContext.BaseDirectory;
        var directory = Path.Combine(root, "TestResults", "post-position-count-performance");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "comparison.json"), JsonSerializer.Serialize(new
        {
            Dataset = "One category; topic/ThreadId 10 has 50,000 posts; 15 other threads have 10,000 each; BodyHtml is 512 Unicode characters; FULLSCAN statistics.",
            PostId = postId,
            Before = before,
            After = after,
        }));

        Assert.Equal(45000, before.Count);
        Assert.Equal(before.Count, after.Count);
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        var seekIndexes = after.ActualPlans.Select(XDocument.Parse)
            .SelectMany(plan => plan.Descendants(ns + "RelOp"))
            .Where(node => (string?)node.Attribute("PhysicalOp") == "Index Seek")
            .SelectMany(node => node.Descendants(ns + "Object"))
            .Select(node => (string?)node.Attribute("Index"))
            .ToArray();
        Assert.Contains("[IX_ModernForumPost_Thread_Posted]", seekIndexes);
        Assert.True(before.LogicalReads > 0 && after.LogicalReads > 0, "STATISTICS IO must report reads for both queries.");
        Assert.True(after.LogicalReads < before.LogicalReads,
            $"Expected fewer logical reads: before={before.LogicalReads}, after={after.LogicalReads}");
    }

    private async Task<CountDiagnostic> MeasureCountAsync(string sql, int postId)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var messages = new List<string>();
        connection.InfoMessage += (_, args) => messages.Add(args.Message);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET STATISTICS IO ON; SET STATISTICS XML ON; {sql}";
        command.Parameters.Add(new SqlParameter("@topic", System.Data.SqlDbType.Int) { Value = 10 });
        command.Parameters.Add(new SqlParameter("@postId", System.Data.SqlDbType.Int) { Value = postId });
        var timer = Stopwatch.StartNew();
        await using var reader = await command.ExecuteReaderAsync();
        int? count = null;
        var plans = new List<string>();
        // The lookup can emit its plan before the count; drain all results and IO messages.
        do
        {
            while (await reader.ReadAsync())
            {
                if (reader.GetFieldType(0) == typeof(int))
                {
                    Assert.Null(count);
                    count = reader.GetInt32(0);
                }
                else
                {
                    plans.Add(reader.GetString(0));
                }
            }
        }
        while (await reader.NextResultAsync());

        timer.Stop();
        Assert.True(count.HasValue, "Expected a COUNT(*) result.");
        Assert.NotEmpty(plans);
        var logicalReads = messages.SelectMany(message => Regex.Matches(message, @"\blogical reads (\d+)",
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Cast<Match>())
            .Sum(match => long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        return new CountDiagnostic(count.Value, logicalReads, timer.Elapsed.TotalMilliseconds, messages.ToArray(), plans.ToArray());
    }

    private sealed record CountDiagnostic(int Count, long LogicalReads, double ElapsedMs, string[] StatisticsIo, string[] ActualPlans);
}
