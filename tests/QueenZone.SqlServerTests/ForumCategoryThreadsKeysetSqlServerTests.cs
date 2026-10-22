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

public sealed partial class ForumCategoryThreadsKeysetSqlServerTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string Procedure = "ModernForum_GetCategoryThreadsPage";
    private const string OldProcedure = "ModernForum_GetCategoryThreadsPage_Old";
    private const int ThreadCount = 120_000;
    // ModernForumRepository accepts the caller's size; ForumRoutes.TopicsPageSize is 25.
    private const int PageSize = 25;
    private readonly ModernForumSqlServerDatabase database = new();

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        await database.SeedCategoryAsync(1, 11, "Large category", 1);
        await database.SeedCategoryAsync(2, 22, "Other category", 2);
        var oldSql = new HideSuspendedMemberTopics().UpOperations.OfType<SqlOperation>()
            .Single(operation => operation.Sql.Contains($"CREATE OR ALTER PROCEDURE dbo.{Procedure}", StringComparison.Ordinal))
            .Sql.Replace($"dbo.{Procedure}", $"dbo.{OldProcedure}", StringComparison.Ordinal);
        await database.ExecuteAsync(oldSql);
        await database.ExecuteAsync("""
            ;WITH Digits AS (
                SELECT n FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(n)
            ), Numbers AS (
                SELECT TOP (120200) CONVERT(int, ROW_NUMBER() OVER (
                    ORDER BY a.n, b.n, c.n, d.n, e.n, f.n)) AS n
                FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c
                    CROSS JOIN Digits d CROSS JOIN Digits e CROSS JOIN Digits f
            )
            INSERT dbo.ModernForumThread
                (LegacyTopicId, LegacyForumId, CategoryId, Title, StartedByLegacyUserId,
                 StartedByDisplayName, LastActivityAt, ReplyCount, IsSticky, IsLegacyTopicStarter,
                 StartedByUserValidated, LegacyDiscography, IsHidden, StarterAttachCount)
            SELECT 3 * n + 7, CASE WHEN n <= 120000 THEN 11 ELSE 22 END,
                CASE WHEN n <= 120000 THEN 1 ELSE 2 END,
                CONCAT(N'  Queen & Freddie — topic ', n, N'  '),
                CASE WHEN n % 7 = 0 THEN NULL ELSE n END, N'  Synthetic author  ',
                CASE WHEN n % 11 = 0 THEN NULL
                    ELSE DATEADD(second, n / 100, CONVERT(datetime2, '20260101')) END,
                n % 13, CASE WHEN n % 101 = 0 THEN 1 ELSE 0 END,
                CASE WHEN n % 17 = 0 THEN 0 ELSE 1 END,
                CASE WHEN n % 19 = 0 THEN 0 WHEN n % 23 = 0 THEN NULL ELSE 1 END,
                0, CASE WHEN n % 10 = 0 THEN 1 ELSE 0 END, 0
            FROM Numbers;
            """);
        await database.RefreshReadStatsAsync();
        await database.ExecuteAsync("""
            UPDATE STATISTICS dbo.ModernForumThread WITH FULLSCAN;
            UPDATE STATISTICS dbo.ModernForumCategory WITH FULLSCAN;
            UPDATE STATISTICS dbo.ModernForumCategoryReadStats WITH FULLSCAN;
            """);
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task Keyset_preserves_category_pages_and_repository_rows_with_fewer_logical_reads()
    {
        var expected = ExpectedRows(1, ThreadCount);
        var stickyCount = expected.Count(row => row.Sticky == 1);
        var lastPage = (expected.Count + PageSize - 1) / PageSize;
        Assert.InRange(expected.Count % PageSize, 1, PageSize - 1);
        var pages = new HashSet<int> { 1, lastPage / 2, lastPage, lastPage + 1, lastPage + 2 };
        AddBoundaryPages(pages, stickyCount);
        // Exercise NULL transitions in both sticky groups, including a boundary already in NULLs.
        foreach (var sticky in new byte[] { 1, 0 })
        {
            var firstNull = expected.FindIndex(row => row.Sticky == sticky && row.LastActivityAt is null);
            Assert.True(firstNull > 0);
            AddBoundaryPages(pages, firstNull);
        }

        foreach (var page in pages.Order())
        {
            await AssertParityAsync(11, page, expected);
        }

        await AssertParityAsync(22, 1, ExpectedRows(ThreadCount + 1, 200));
        await AssertParityAsync(999, 2, []);

        var deepestFullPage = expected.Count / PageSize;
        var oldPage = await ReadPageAsync(OldProcedure, 11, deepestFullPage);
        var repository = new ModernForumRepository(database.CreateContext());
        var repositoryPage = await repository.GetCategoryTopicsPageAsync(11, deepestFullPage, PageSize);
        Assert.Equal(oldPage.TotalRecords, repositoryPage.TotalCount);
        Assert.Equal(deepestFullPage, repositoryPage.Page);
        Assert.Equal(PageSize, repositoryPage.PageSize);
        Assert.Equal(oldPage.Rows.Select(row => new ForumTopicItem(
            row.TopicId, row.Title.Trim(), row.LastActivityAt ?? DateTime.MinValue,
            row.Username.Trim(), row.ReplyCount, row.LastPostUsername, row.Sticky == 1)), repositoryPage.Topics);

        var original = await ReadPageAsync(OldProcedure, 11, deepestFullPage, measure: true);
        var repaired = await ReadPageAsync(Procedure, 11, deepestFullPage, measure: true);
        Assert.Equal(PageSize, original.Rows.Count);
        Assert.Equal(original.Rows, repaired.Rows);
        Assert.Equal(original.TotalRecords, repaired.TotalRecords);
        var comparison = new
        {
            Dataset = new
            {
                CategoryThreads = ThreadCount,
                OtherCategoryThreads = 200,
                VisibleThreads = expected.Count,
                Description = "LegacyTopicId=3*n+7; hidden n%10=0; non-starters n%17=0; " +
                    "unvalidated n%19=0 or n%23=0 (NULL); sticky n%101=0; NULL activity n%11=0; " +
                    "100-row activity ties; padded Unicode titles; nullable author IDs; " +
                    "cached read stats; FULLSCAN thread/category/read-stat statistics",
            },
            Page = deepestFullPage,
            Offset = (deepestFullPage - 1) * PageSize,
            PageSize,
            Original = new { RowCount = original.Rows.Count, original.TotalRecords, original.LogicalReads, original.StatisticsIo },
            Repaired = new { RowCount = repaired.Rows.Count, repaired.TotalRecords, repaired.LogicalReads, repaired.StatisticsIo },
        };
        var json = JsonSerializer.Serialize(comparison);
        output.WriteLine(json);
        output.WriteLine($"Deepest full category page {deepestFullPage}, {PageSize} rows: " +
            $"old={original.LogicalReads} logical reads, new={repaired.LogicalReads} logical reads; " +
            $"{expected.Count} visible of {ThreadCount} category threads plus 200 other-category threads.");
        var root = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? AppContext.BaseDirectory;
        var directory = Path.Combine(root, "TestResults", "category-keyset-performance");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "comparison.json"), json);
        Assert.True(original.LogicalReads > 0, "No original STATISTICS IO logical reads were captured.");
        Assert.True(repaired.LogicalReads > 0, "No repaired STATISTICS IO logical reads were captured.");
        Assert.True(repaired.LogicalReads < original.LogicalReads,
            $"Expected fewer logical reads: old={original.LogicalReads}, new={repaired.LogicalReads}.");

        // Preserve the exact-count fallback when the category has no cached read statistics.
        await database.ExecuteAsync("DELETE FROM dbo.ModernForumCategoryReadStats WHERE CategoryId = 1;");
        await AssertParityAsync(11, deepestFullPage, expected);
    }

    private static void AddBoundaryPages(HashSet<int> pages, int offset)
    {
        var page = offset / PageSize + 1;
        pages.Add(page);
        pages.Add(page + 1);
    }

    private static List<TopicRow> ExpectedRows(int start, int count) =>
        Enumerable.Range(start, count)
            .Where(n => n % 10 != 0 && n % 17 != 0 && n % 19 != 0 && n % 23 != 0)
            .Select(n => new TopicRow(0, 3 * n + 7, $"  Queen & Freddie — topic {n}  ",
                n % 11 == 0 ? null : new DateTime(2026, 1, 1).AddSeconds(n / 100),
                n % 7 == 0 ? null : n, "  Synthetic author  ", n % 13, null,
                (byte)(n % 101 == 0 ? 1 : 0)))
            .OrderByDescending(row => row.Sticky)
            .ThenByDescending(row => row.LastActivityAt)
            .ThenBy(row => row.TopicId)
            .ToList();

    private async Task AssertParityAsync(int forumId, int page, List<TopicRow> expected)
    {
        var original = await ReadPageAsync(OldProcedure, forumId, page);
        var repaired = await ReadPageAsync(Procedure, forumId, page);
        Assert.Equal(expected.Count, original.TotalRecords);
        Assert.Equal(original.TotalRecords, repaired.TotalRecords);
        Assert.Equal(expected.Skip((page - 1) * PageSize).Take(PageSize), original.Rows);
        Assert.Equal(original.Rows, repaired.Rows);
    }

    private async Task<PageDiagnostic> ReadPageAsync(string procedure, int forumId, int page, bool measure = false)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var messages = new List<string>();
        connection.InfoMessage += (_, args) => messages.Add(args.Message);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = $"{(measure ? "SET STATISTICS IO ON; " : string.Empty)}" +
            $"EXEC dbo.{procedure} @CurrentPage = @page, @PageSize = @pageSize, " +
            "@Q_FORUM_ID = @forumId, @TotalRecords = @total OUTPUT;";
        command.Parameters.Add(new SqlParameter("@page", SqlDbType.Int) { Value = page });
        command.Parameters.Add(new SqlParameter("@pageSize", SqlDbType.Int) { Value = PageSize });
        command.Parameters.Add(new SqlParameter("@forumId", SqlDbType.Int) { Value = forumId });
        var total = command.Parameters.Add("@total", SqlDbType.Int);
        total.Direction = ParameterDirection.Output;
        var rows = new List<TopicRow>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            string[] names = ["Id", "Q_FORUM_TOPIC_ID", "TOPIC_SUBJECT", "TOPIC_LAST_POST", "USER_ID",
                "USERNAME", "NUMBEROFREPLIES", "LAST_POST_USERNAME", "STICKY"];
            Type[] types = [typeof(int), typeof(int), typeof(string), typeof(DateTime), typeof(int),
                typeof(string), typeof(int), typeof(string), typeof(byte)];
            Assert.Equal(names.Length, reader.FieldCount);
            for (var column = 0; column < names.Length; column++)
            {
                Assert.Equal(names[column], reader.GetName(column));
                Assert.Equal(types[column], reader.GetFieldType(column));
            }

            while (await reader.ReadAsync())
            {
                rows.Add(new TopicRow(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.GetString(5), reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetByte(8)));
            }

            // Drain the final result before reading output parameters and STATISTICS IO messages.
            Assert.False(await reader.NextResultAsync());
        }

        var reads = messages.SelectMany(message => LogicalReadsPattern().Matches(message).Cast<Match>())
            .Sum(match => long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        return new PageDiagnostic(rows, (int)total.Value, reads, messages.ToArray());
    }

    [GeneratedRegex(@"\blogical reads ([0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalReadsPattern();

    private sealed record TopicRow(int Id, int TopicId, string Title, DateTime? LastActivityAt,
        int? UserId, string Username, int ReplyCount, string? LastPostUsername, byte Sticky);

    private sealed record PageDiagnostic(List<TopicRow> Rows, int TotalRecords, long LogicalReads, string[] StatisticsIo);
}
