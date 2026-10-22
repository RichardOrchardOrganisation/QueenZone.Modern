using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;
using QueenZone.Data.Migrations;
using Xunit.Abstractions;

namespace QueenZone.SqlServerTests;

public sealed partial class ForumSitemapKeysetSqlServerTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Procedure = "ModernForum_GetTopicSitemapPage";
    private const string OldProcedure = "ModernForum_GetTopicSitemapPage_Old";
    private const int ThreadCount = 150_000;
    private readonly ModernForumSqlServerDatabase database = new();

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        await database.SeedCategoryAsync(1, 1, "Public", 1);
        await database.SeedCategoryAsync(2, 0, "Unresolvable zero", 2);
        await database.SeedCategoryAsync(3, -1, "Unresolvable negative", 3);
        // Keep the exact #2366 definition, including its old non-covering index hint.
        var oldSql = new AlignForumSitemapVisibility().UpOperations.OfType<SqlOperation>()
            .Single(operation => operation.Sql.Contains($"CREATE OR ALTER PROCEDURE dbo.{Procedure}", StringComparison.Ordinal))
            .Sql.Replace($"dbo.{Procedure}", $"dbo.{OldProcedure}", StringComparison.Ordinal);
        await database.ExecuteAsync(oldSql);
        await database.ExecuteAsync("""
            ;WITH Digits AS (
                SELECT n FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(n)
            ), Numbers AS (
                SELECT TOP (150000) CONVERT(int, ROW_NUMBER() OVER (
                    ORDER BY a.n, b.n, c.n, d.n, e.n, f.n)) AS n
                FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c
                    CROSS JOIN Digits d CROSS JOIN Digits e CROSS JOIN Digits f
            )
            INSERT dbo.ModernForumThread
                (LegacyTopicId, LegacyForumId, CategoryId, Title, StartedByDisplayName,
                 LastActivityAt, ReplyCount, IsSticky, IsLegacyTopicStarter,
                 LegacyDiscography, IsHidden, StarterAttachCount)
            SELECT 3 * n + 7,
                CASE WHEN n % 82 = 0 THEN -1 WHEN n % 41 = 0 THEN 0 ELSE 1 END,
                CASE WHEN n % 86 = 0 THEN 3 WHEN n % 43 = 0 THEN 2 ELSE 1 END,
                CASE WHEN n % 74 = 0 THEN N'' WHEN n % 37 = 0 THEN N'   '
                    ELSE CONCAT(N'  Queen & Freddie — topic ', n, N'  ') END,
                N'Synthetic author',
                CASE WHEN n % 11 = 0 THEN NULL ELSE DATEADD(second, n, CONVERT(datetime2, '20260101')) END,
                0, 0, 1, 0, CASE WHEN n % 10 = 0 THEN 1 ELSE 0 END, 0
            FROM Numbers;
            UPDATE STATISTICS dbo.ModernForumThread WITH FULLSCAN;
            UPDATE STATISTICS dbo.ModernForumCategory WITH FULLSCAN;
            """);
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task Keyset_preserves_offset_pages_and_repository_rows_with_fewer_logical_reads()
    {
        var repository = new ModernForumRepository(database.CreateContext());
        var visibleCount = await repository.GetTopicSitemapCountAsync();
        var expectedVisibleCount = Enumerable.Range(1, ThreadCount)
            .Count(n => n % 10 != 0 && n % 37 != 0 && n % 41 != 0 && n % 43 != 0);
        Assert.Equal(expectedVisibleCount, visibleCount);
        Assert.InRange(visibleCount, 100_001, ThreadCount - 1);

        // Web is not referenced: 10,000 is the current ForumSitemapLimits.MaxUrlsPerFile.
        // Also exercise the requested 50,000-row export size without changing that limit.
        foreach (var pageSize in new[] { 50_000, 10_000 })
        {
            var partialOffset = visibleCount / pageSize * pageSize;
            Assert.InRange(visibleCount - partialOffset, 1, pageSize - 1);
            foreach (var offset in new[] { 0, 50_000, 100_000, partialOffset, visibleCount, visibleCount + 1 }.Distinct())
            {
                var oldPage = await ReadPageAsync(OldProcedure, offset, pageSize);
                var newPage = await ReadPageAsync(Procedure, offset, pageSize);
                Assert.Equal(Math.Min(pageSize, Math.Max(visibleCount - offset, 0)), oldPage.Rows.Count);
                Assert.Equal(oldPage.Rows, newPage.Rows);
                var repositoryPage = await repository.GetTopicSitemapPageAsync(offset, pageSize);
                Assert.Equal(oldPage.Rows, repositoryPage.ToList());

                if (offset > 0 && offset <= visibleCount)
                {
                    var boundary = await ReadPageAsync(OldProcedure, offset - 1, 1);
                    // A supplied cursor takes precedence over the offset, even past the end.
                    var cursorPage = await ReadPageAsync(Procedure, int.MaxValue, pageSize,
                        Assert.Single(boundary.Rows).TopicId);
                    Assert.Equal(oldPage.Rows, cursorPage.Rows);
                }
            }
        }

        // A cursor in a gap need not name a row. It must start at the next visible key.
        var firstPage = await ReadPageAsync(OldProcedure, 0, 2);
        var gapPage = await ReadPageAsync(Procedure, 0, 1, firstPage.Rows[0].TopicId + 1);
        Assert.Equal(firstPage.Rows[1], Assert.Single(gapPage.Rows));
        Assert.Empty((await ReadPageAsync(Procedure, 0, 1, int.MaxValue)).Rows);

        const int measuredOffset = 100_000;
        const int measuredPageSize = 50_000;
        var original = await ReadPageAsync(OldProcedure, measuredOffset, measuredPageSize, measure: true);
        var repaired = await ReadPageAsync(Procedure, measuredOffset, measuredPageSize, measure: true);
        Assert.Equal(original.Rows, repaired.Rows);
        var comparison = new
        {
            Dataset = new
            {
                Threads = ThreadCount,
                VisibleThreads = visibleCount,
                Description = "LegacyTopicId=3*n+7; hidden n%10=0; blank title n%37=0; " +
                    "nonpositive thread forum n%41=0; nonpositive category forum n%43=0; " +
                    "padded Unicode titles; nullable LastActivityAt; FULLSCAN thread/category statistics",
            },
            Offset = measuredOffset,
            PageSize = measuredPageSize,
            Original = new { RowCount = original.Rows.Count, original.LogicalReads, original.StatisticsIo },
            Repaired = new { RowCount = repaired.Rows.Count, repaired.LogicalReads, repaired.StatisticsIo },
        };
        var json = JsonSerializer.Serialize(comparison);
        output.WriteLine(json);
        var root = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? AppContext.BaseDirectory;
        var directory = Path.Combine(root, "TestResults", "sitemap-keyset-performance");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "comparison.json"), json);
        // Persist diagnostics before the performance assertion, including on a regression.
        Assert.True(original.LogicalReads > 0, "No original STATISTICS IO logical reads were captured.");
        Assert.True(repaired.LogicalReads > 0, "No repaired STATISTICS IO logical reads were captured.");
        Assert.True(repaired.LogicalReads < original.LogicalReads,
            $"Expected fewer logical reads: old={original.LogicalReads}, new={repaired.LogicalReads}.");
    }

    private async Task<PageDiagnostic> ReadPageAsync(
        string procedure, int offset, int pageSize, int? afterTopicId = null, bool measure = false)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var messages = new List<string>();
        connection.InfoMessage += (_, args) => messages.Add(args.Message);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = $"{(measure ? "SET STATISTICS IO ON; " : string.Empty)}" +
            $"EXEC dbo.{procedure} @Offset = @offset, @PageSize = @pageSize" +
            (afterTopicId.HasValue ? ", @AfterTopicId = @afterTopicId;" : ";");
        command.Parameters.Add(new SqlParameter("@offset", SqlDbType.Int) { Value = offset });
        command.Parameters.Add(new SqlParameter("@pageSize", SqlDbType.Int) { Value = pageSize });
        if (afterTopicId.HasValue)
        {
            command.Parameters.Add(new SqlParameter("@afterTopicId", SqlDbType.Int) { Value = afterTopicId.Value });
        }

        var rows = new List<ForumTopicSitemapItem>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.Equal(3, reader.FieldCount);
            Assert.Equal("TopicId", reader.GetName(0));
            Assert.Equal("Title", reader.GetName(1));
            Assert.Equal("LastActivityAt", reader.GetName(2));
            while (await reader.ReadAsync())
            {
                rows.Add(new ForumTopicSitemapItem(reader.GetInt32(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetDateTime(2)));
            }

            // Drain the final result so all STATISTICS IO InfoMessage events arrive.
            Assert.False(await reader.NextResultAsync());
        }

        var reads = messages.SelectMany(message => LogicalReadsPattern().Matches(message).Cast<Match>())
            .Sum(match => long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        return new PageDiagnostic(rows, reads, messages.ToArray());
    }

    [GeneratedRegex(@"\blogical reads ([0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalReadsPattern();

    private sealed record PageDiagnostic(List<ForumTopicSitemapItem> Rows, long LogicalReads, string[] StatisticsIo);
}
