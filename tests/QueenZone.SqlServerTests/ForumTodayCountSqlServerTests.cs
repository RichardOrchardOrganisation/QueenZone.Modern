using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QueenZone.Data;
using Xunit.Abstractions;

namespace QueenZone.SqlServerTests;

public sealed class ForumTodayCountSqlServerTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const string VisibleIndex = "[IX_ModernForumPost_PostedAt_Visible]";
    private static readonly DateTime Today = new(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
    private readonly ModernForumSqlServerDatabase database = new();

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task Today_count_preserves_parity_and_seeks_with_simple_and_forced_parameterization()
    {
        var comparisons = new List<Comparison>();
        var capture = new CommandCapture();
        await using var context = new QueenZoneDbContext(
            new DbContextOptionsBuilder<QueenZoneDbContext>()
                .UseSqlServer(database.ConnectionString)
                .AddInterceptors(capture).Options);
        try
        {
            await SeedAsync();
            foreach (var columnScale in new[] { 0, 7 })
            {
                await ConfigurePrecisionAsync(columnScale);
                foreach (var mode in new[] { "SIMPLE", "FORCED" })
                {
                    await database.ExecuteAsync($"ALTER DATABASE CURRENT SET PARAMETERIZATION {mode};");
                    // Includes empty future day, inclusive midnight, previous day and
                    // broad archive boundary. The contract deliberately has no upper bound.
                    foreach (var boundary in new[] { Today, Today.AddDays(1), Today.AddDays(-1), Today.AddDays(-731) })
                    {
                        var todayStartUtc = boundary;
                        var original = await context.ModernForumPosts
                            .Where(post => post.PostedAt != null && post.PostedAt >= todayStartUtc && !post.IsHidden)
                            .CountAsync();
                        var originalCommand = capture.Last!;
                        var service = new EfLiveActivityQueryService(context,
                            new FakeTimeProvider(new DateTimeOffset(boundary.AddHours(15))));
                        var repaired = await service.GetNewForumRepliesTodayAsync();
                        var repairedCommand = capture.Last!;
                        Assert.Equal(original, repaired);
                        if (boundary == Today)
                        {
                            Assert.Equal(450, repaired); // 500 today, 50 hidden; includes midnight.
                            var oldPlan = await MeasureAsync(originalCommand);
                            var newPlan = await MeasureAsync(repairedCommand);
                            var candidates = new Dictionary<string, Diagnostic>();
                            foreach (var parameterScale in new byte[] { 0, 7 })
                            {
                                foreach (var predicate in new[] { "IsHidden = 0", "IsHidden = CAST(0 AS bit)", "IsHidden = @hidden" })
                                {
                                    var parameters = new List<SqlParameter>
                                    {
                                        new("@todayStartUtc", SqlDbType.DateTime2) { Scale = parameterScale, Value = Today },
                                    };
                                    if (predicate.Contains('@'))
                                    {
                                        parameters.Add(new SqlParameter("@hidden", SqlDbType.Bit) { Value = false });
                                    }

                                    candidates[$"{predicate}; datetime2({parameterScale})"] = await MeasureAsync(
                                        new CapturedCommand($"SELECT COUNT(*) FROM dbo.ModernForumPost WHERE PostedAt IS NOT NULL AND PostedAt >= @todayStartUtc AND {predicate}", parameters.ToArray()));
                                }
                            }

                            comparisons.Add(new Comparison(columnScale, mode, oldPlan, newPlan, candidates));
                            output.WriteLine($"datetime2({columnScale}) {mode}: old {oldPlan.LogicalReads} reads [{Describe(oldPlan)}]; new {newPlan.LogicalReads} reads [{Describe(newPlan)}]");
                        }
                        else if (boundary > Today)
                        {
                            Assert.Equal(0, repaired);
                        }
                    }
                }
            }

            // The finally block retains every diagnostic even if a FORCED plan
            // fails these assertions, providing evidence for the next query shape.
            foreach (var comparison in comparisons)
            {
                Assert.Equal(comparison.Original.Count, comparison.Repaired.Count);
                Assert.Contains(comparison.Repaired.Accesses,
                    access => access.Operation == "Index Seek" && access.Index == VisibleIndex);
                if (comparison.Original.Accesses.Any(access => access.Operation.Contains("Scan", StringComparison.Ordinal)))
                {
                    Assert.True(comparison.Repaired.LogicalReads < comparison.Original.LogicalReads,
                        $"{comparison.Mode}/datetime2({comparison.ColumnScale}): new reads must improve on the old scan.");
                }

                Assert.All(comparison.Candidates.Values, candidate => Assert.Equal(450, candidate.Count));
            }
        }
        finally
        {
            try
            {
                await database.ExecuteAsync("ALTER DATABASE CURRENT SET PARAMETERIZATION SIMPLE;");
            }
            finally
            {
                var root = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? AppContext.BaseDirectory;
                var directory = Path.Combine(root, "TestResults", "today-count-performance");
                Directory.CreateDirectory(directory);
                await File.WriteAllTextAsync(Path.Combine(directory, "comparison.json"), JsonSerializer.Serialize(new
                {
                    Dataset = "300000 posts: 299500 over preceding 730 days (every 997th NULL); 500 today including exactly midnight; every 10th hidden (50 today); BodyHtml REPLICATE(N'x',512); FULLSCAN statistics; existing production filtered index without INCLUDEs",
                    Comparisons = comparisons,
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
    }

    private async Task SeedAsync()
    {
        await database.SeedCategoryAsync(1, 1, "General", 1);
        await database.SeedThreadAsync(10, 10, 1, 1, "Today count", "Tester");
        await database.ExecuteAsync("""
            WITH digits(n) AS (SELECT n FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(n)),
            numbers AS (
                SELECT TOP (300000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
                FROM digits a CROSS JOIN digits b CROSS JOIN digits c
                CROSS JOIN digits d CROSS JOIN digits e CROSS JOIN digits f
            )
            INSERT dbo.ModernForumPost
                (LegacyPostId, LegacyThreadTopicId, ThreadId, LegacyForumId,
                 AuthorDisplayName, BodyHtml, PostedAt, IsHidden, LegacyDiscography, AttachCount)
            SELECT CONVERT(int, n), 10, 10, 1, N'Tester', REPLICATE(N'x', 512),
                CASE WHEN n > 299500 THEN DATEADD(second, CONVERT(int, n - 299501), CONVERT(datetime2, '2026-10-22T00:00:00'))
                     WHEN n % 997 = 0 THEN NULL
                     ELSE DATEADD(day, -CONVERT(int, 1 + n % 730), CONVERT(datetime2, '2026-10-22T00:00:00')) END,
                CASE WHEN n % 10 = 0 THEN 1 ELSE 0 END, 0, 0
            FROM numbers;
            """);
    }

    private Task ConfigurePrecisionAsync(int scale) => database.ExecuteAsync($"""
        DROP INDEX IF EXISTS IX_ModernForumPost_PostedAt_Visible ON dbo.ModernForumPost;
        DROP INDEX IX_ModernForumPost_VisibleDiscussion ON dbo.ModernForumPost;
        DROP INDEX IX_ModernForumPost_Thread_Posted ON dbo.ModernForumPost;
        ALTER TABLE dbo.ModernForumPost ALTER COLUMN PostedAt datetime2({scale}) NULL;
        CREATE INDEX IX_ModernForumPost_VisibleDiscussion
            ON dbo.ModernForumPost (LegacyThreadTopicId, PostedAt, LegacyPostId) WHERE IsHidden = 0;
        CREATE INDEX IX_ModernForumPost_Thread_Posted
            ON dbo.ModernForumPost (ThreadId, LegacyPostId) INCLUDE (PostedAt, AuthorDisplayName, IsHidden);
        CREATE INDEX IX_ModernForumPost_PostedAt_Visible
            ON dbo.ModernForumPost (PostedAt) WHERE IsHidden = 0;
        UPDATE STATISTICS dbo.ModernForumPost WITH FULLSCAN;
        """);

    private async Task<Diagnostic> MeasureAsync(CapturedCommand captured)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        var messages = new List<string>();
        connection.InfoMessage += (_, args) => messages.Add(args.Message);
        // Separate SET batch preserves the exact EF batch shape for parameterization.
        await using (var settings = connection.CreateCommand())
        {
            settings.CommandText = "SET STATISTICS IO ON; SET STATISTICS XML ON;";
            await settings.ExecuteNonQueryAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = captured.Sql;
        command.Parameters.AddRange(captured.Parameters.Select(Clone).ToArray());
        int count;
        string plan;
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            count = reader.GetInt32(0);
            Assert.True(await reader.NextResultAsync());
            Assert.True(await reader.ReadAsync());
            plan = reader.GetString(0);
            while (await reader.NextResultAsync())
            {
                while (await reader.ReadAsync())
                {
                    // Drain all results so every STATISTICS IO message is received.
                }
            }
        }

        var matches = messages.SelectMany(message => Regex.Matches(message, @"\blogical reads (\d+)", RegexOptions.CultureInvariant).Cast<Match>()).ToArray();
        Assert.NotEmpty(matches);
        var reads = matches.Sum(match => long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
        XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";
        var accesses = XDocument.Parse(plan).Descendants(ns + "RelOp")
            .SelectMany(operation => operation.Elements(ns + "IndexScan").Elements(ns + "Object")
                .Select(index => new PlanAccess((string?)operation.Attribute("PhysicalOp") ?? "",
                    (string?)index.Attribute("Index") ?? ""))).ToArray();
        return new Diagnostic(count, reads, accesses, captured.Sql,
            captured.Parameters.Select(parameter => new ParameterDiagnostic(parameter.ParameterName,
                parameter.SqlDbType.ToString(), parameter.Precision, parameter.Scale, parameter.Value)).ToArray(), messages.ToArray(), plan);
    }

    private static string Describe(Diagnostic diagnostic) => string.Join(", ", diagnostic.Accesses.Select(access => $"{access.Operation} {access.Index}"));
    private static SqlParameter Clone(SqlParameter parameter) => (SqlParameter)((ICloneable)parameter).Clone();

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public CapturedCommand? Last { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Last = new CapturedCommand(command.CommandText, command.Parameters.Cast<SqlParameter>().Select(Clone).ToArray());
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record CapturedCommand(string Sql, SqlParameter[] Parameters);
    private sealed record PlanAccess(string Operation, string Index);
    private sealed record ParameterDiagnostic(string Name, string Type, byte Precision, byte Scale, object? Value);
    private sealed record Diagnostic(int Count, long LogicalReads, PlanAccess[] Accesses, string Sql,
        ParameterDiagnostic[] Parameters, string[] StatisticsIo, string ActualPlan);
    private sealed record Comparison(int ColumnScale, string Mode, Diagnostic Original, Diagnostic Repaired,
        Dictionary<string, Diagnostic> Candidates);
}
