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
using QueenZone.Data.Entities;
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
    public async Task Today_count_preserves_parity_and_seeks_regardless_of_parameter_scale()
    {
        var comparisons = new List<Comparison>();
        var capture = new CommandCapture();
        await using var context = new QueenZoneDbContext(
            new DbContextOptionsBuilder<QueenZoneDbContext>()
                .UseSqlServer(database.ConnectionString)
                .AddInterceptors(capture).Options);
        try
        {
            var entity = context.Model.FindEntityType(typeof(ModernForumPostEntity));
            Assert.NotNull(entity);
            Assert.Equal("ModernForumPost", entity.GetTableName());
            Assert.Equal("dbo", entity.GetSchema() ?? "dbo");

            await SeedAsync();
            await ConfigurePrecisionAsync();
            await database.ExecuteAsync("ALTER DATABASE CURRENT SET PARAMETERIZATION SIMPLE;");
            // Execute both EF batches for a broad archive boundary before today's
            // measurement. The isolated database has no earlier count plans.
            var todayStartUtc = Today.AddDays(-700);
            await context.ModernForumPosts
                .Where(post => post.PostedAt != null && post.PostedAt >= todayStartUtc && !post.IsHidden)
                .CountAsync();
            var originalCommand = ForBoundary(capture.Last!, Today);
            var service = new EfLiveActivityQueryService(context,
                new FakeTimeProvider(new DateTimeOffset(todayStartUtc.AddHours(15))));
            await service.GetNewForumRepliesTodayAsync();
            var repairedCommand = ForBoundary(capture.Last!, Today);
            var typedWithoutHintCommand = repairedCommand with
            {
                Sql = repairedCommand.Sql.Replace("OPTION (RECOMPILE)", "", StringComparison.Ordinal).TrimEnd(),
            };
            var originalPlan = await MeasureAsync(originalCommand);
            var repairedPlan = await MeasureAsync(repairedCommand);
            var typedWithoutHintPlan = await MeasureAsync(typedWithoutHintCommand);
            var coldWithoutHintPlan = await MeasureAsync(typedWithoutHintCommand, warmCache: false);
            var originalColdPlan = await MeasureAsync(originalCommand, warmCache: false);
            comparisons.Add(new Comparison(0, "SIMPLE", originalPlan, repairedPlan, typedWithoutHintPlan,
                coldWithoutHintPlan, originalColdPlan));
            foreach (var (name, diagnostic) in new[]
            {
                ("Old EF warmed", originalPlan),
                ("Old EF cold", originalColdPlan),
                ("Without hint warmed", typedWithoutHintPlan),
                ("Without hint cold", coldWithoutHintPlan),
                ("With RECOMPILE", repairedPlan),
            })
            {
                output.WriteLine($"SIMPLE datetime2(0): {name}: {diagnostic.LogicalReads} reads [{Describe(diagnostic)}]; " +
                    $"plan parameters: {JsonSerializer.Serialize(diagnostic.PlanParameters)}");
            }

            // Includes empty future day, inclusive midnight, previous day and
            // broad archive boundary. The contract deliberately has no upper bound.
            var clockCases = new[]
            {
                new DateTimeOffset(Today),
                new DateTimeOffset(Today.AddHours(23).AddMinutes(59).AddSeconds(59)),
                new DateTimeOffset(2026, 10, 22, 7, 30, 0, TimeSpan.FromHours(8)),
                new DateTimeOffset(2026, 10, 22, 0, 30, 0, TimeSpan.FromHours(-5)),
                new DateTimeOffset(Today.AddDays(1)),
                new DateTimeOffset(Today.AddDays(-1)),
                new DateTimeOffset(Today.AddDays(-731)),
            };
            foreach (var now in clockCases)
            {
                var clock = new FakeTimeProvider(now);
                todayStartUtc = clock.GetUtcNow().UtcDateTime.Date;
                var original = await context.ModernForumPosts
                    .Where(post => post.PostedAt != null && post.PostedAt >= todayStartUtc && !post.IsHidden)
                    .CountAsync();
                service = new EfLiveActivityQueryService(context, clock);
                var repaired = await service.GetNewForumRepliesTodayAsync();
                Assert.Equal(original, repaired);
                if (todayStartUtc == Today)
                {
                    Assert.Equal(450, repaired); // 500 today, 50 hidden; includes midnight.
                }
                else if (todayStartUtc > Today)
                {
                    Assert.Equal(0, repaired);
                }
            }

            Assert.Equal(450, originalPlan.Count);
            Assert.Equal(originalPlan.Count, repairedPlan.Count);
            Assert.Equal(repairedPlan.Count, typedWithoutHintPlan.Count);
            Assert.Equal(repairedPlan.Count, coldWithoutHintPlan.Count);
            Assert.Equal(repairedPlan.Count, originalColdPlan.Count);
            Assert.Contains(repairedPlan.Accesses, access =>
                access.Operation == "Index Seek" && access.Index == VisibleIndex && access.HasPostedAtRange);
            var planParameter = Assert.Single(coldWithoutHintPlan.PlanParameters,
                parameter => parameter.Name == "@todayStartUtc");
            Assert.False(string.IsNullOrWhiteSpace(planParameter.ParameterDataType));
            Assert.Contains(coldWithoutHintPlan.Accesses, access =>
                access.Operation == "Index Seek" && access.Index == VisibleIndex && access.HasPostedAtParameterRange);
        }
        finally
        {
            var root = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE") ?? AppContext.BaseDirectory;
            var directory = Path.Combine(root, "TestResults", "today-count-performance");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "comparison.json"), JsonSerializer.Serialize(new
            {
                Dataset = "300000 posts: 299500 over preceding 730 days (every 997th NULL; one at previous day 23:59:59); 500 today including exactly midnight; every 10th hidden (50 today); BodyHtml REPLICATE(N'x',512); FULLSCAN statistics; existing production filtered index without INCLUDEs",
                ColumnType = "datetime2(0); EF model remains datetime2(7) (production schema drift)",
                WarmBoundary = Today.AddDays(-700),
                MeasuredBoundary = Today,
                Comparisons = comparisons,
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static CapturedCommand ForBoundary(CapturedCommand captured, DateTime boundary) => captured with
    {
        Parameters = captured.Parameters.Select(parameter =>
        {
            var clone = Clone(parameter);
            if (clone.SqlDbType == SqlDbType.DateTime2)
            {
                clone.Value = boundary;
            }

            return clone;
        }).ToArray(),
    };

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
                     WHEN n = 1 THEN CONVERT(datetime2, '2026-10-21T23:59:59')
                     WHEN n % 997 = 0 THEN NULL
                     ELSE DATEADD(day, -CONVERT(int, 1 + n % 730), CONVERT(datetime2, '2026-10-22T00:00:00')) END,
                CASE WHEN n % 10 = 0 THEN 1 ELSE 0 END, 0, 0
            FROM numbers;
            """);
    }

    private Task ConfigurePrecisionAsync() => database.ExecuteAsync("""
        DROP INDEX IF EXISTS IX_ModernForumPost_PostedAt_Visible ON dbo.ModernForumPost;
        DROP INDEX IX_ModernForumPost_VisibleDiscussion ON dbo.ModernForumPost;
        DROP INDEX IX_ModernForumPost_Thread_Posted ON dbo.ModernForumPost;
        ALTER TABLE dbo.ModernForumPost ALTER COLUMN PostedAt datetime2(0) NULL;
        CREATE INDEX IX_ModernForumPost_VisibleDiscussion
            ON dbo.ModernForumPost (LegacyThreadTopicId, PostedAt, LegacyPostId) WHERE IsHidden = 0;
        CREATE INDEX IX_ModernForumPost_Thread_Posted
            ON dbo.ModernForumPost (ThreadId, LegacyPostId) INCLUDE (PostedAt, AuthorDisplayName, IsHidden);
        CREATE INDEX IX_ModernForumPost_PostedAt_Visible
            ON dbo.ModernForumPost (PostedAt) WHERE IsHidden = 0;
        UPDATE STATISTICS dbo.ModernForumPost WITH FULLSCAN;
        """);

    private async Task<Diagnostic> MeasureAsync(CapturedCommand captured, bool warmCache = true)
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

        // Clear only this test's isolated database cache before measuring a fresh plan.
        if (!warmCache)
        {
            await using var clear = connection.CreateCommand();
            clear.CommandText = "ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;";
            await clear.ExecuteNonQueryAsync();
        }
        else
        {
            await using var warm = connection.CreateCommand();
            warm.CommandTimeout = 120;
            warm.CommandText = captured.Sql;
            warm.Parameters.AddRange(ForBoundary(captured, Today.AddDays(-700)).Parameters);
            await using var reader = await warm.ExecuteReaderAsync();
            do
            {
                while (await reader.ReadAsync())
                {
                    // Drain the warm-up count and plan before collecting today's IO.
                }
            } while (await reader.NextResultAsync());
        }

        messages.Clear();
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
        var document = XDocument.Parse(plan);
        var accesses = document.Descendants(ns + "RelOp")
            .SelectMany(operation => operation.Elements(ns + "IndexScan")
                .SelectMany(scan => scan.Elements(ns + "Object").Select(index =>
                {
                    var seek = scan.Element(ns + "SeekPredicates");
                    var residual = scan.Element(ns + "Predicate");
                    return new PlanAccess((string?)operation.Attribute("PhysicalOp") ?? "",
                        (string?)index.Attribute("Index") ?? "",
                        seek?.ToString(SaveOptions.DisableFormatting) ?? "",
                        residual?.ToString(SaveOptions.DisableFormatting) ?? "",
                        HasPostedAtRange(seek, ns, allowConstant: true),
                        HasPostedAtRange(seek, ns, allowConstant: false), HasPostedAtParameterResidual(residual, ns),
                        HasPostedAtNotNullRange(seek, ns));
                }))).ToArray();
        return new Diagnostic(count, reads, accesses, captured.Sql,
            captured.Parameters.Select(parameter => new ParameterDiagnostic(parameter.ParameterName,
                parameter.SqlDbType.ToString(), parameter.Precision, parameter.Scale, parameter.Value)).ToArray(),
            document.Descendants(ns + "Intrinsic").Any(element =>
                string.Equals((string?)element.Attribute("FunctionName"), "GetRangeThroughConvert", StringComparison.OrdinalIgnoreCase)),
            document.Descendants(ns + "ParameterList").Elements(ns + "ColumnReference")
                .Select(parameter => new PlanParameterDiagnostic((string?)parameter.Attribute("Column") ?? "",
                    (string?)parameter.Attribute("ParameterDataType"))).ToArray(),
            messages.ToArray(), plan);
    }

    private static bool HasPostedAtRange(XElement? seek, XNamespace ns, bool allowConstant) =>
        seek?.Descendants().Any(range =>
            (range.Name == ns + "StartRange" || range.Name == ns + "Start") &&
            ((string?)range.Attribute("ScanType") is "GE" or "GT") &&
            range.Elements(ns + "RangeColumns").Descendants(ns + "ColumnReference")
                .Any(column => (string?)column.Attribute("Column") == "PostedAt") &&
            // RECOMPILE embeds the parameter value, so the range bound may be a Const.
            (range.Elements(ns + "RangeExpressions").Descendants(ns + "ColumnReference")
                .Any(column => (string?)column.Attribute("Column") == "@todayStartUtc")
             || allowConstant && range.Elements(ns + "RangeExpressions").Descendants(ns + "Const")
                .Any(value => (string?)value.Attribute("ConstValue") != "NULL"))) == true;

    private static bool HasPostedAtNotNullRange(XElement? seek, XNamespace ns) =>
        seek?.Descendants(ns + "StartRange").Any(range =>
            (string?)range.Attribute("ScanType") == "GT" &&
            range.Elements(ns + "RangeColumns").Descendants(ns + "ColumnReference")
                .Any(column => (string?)column.Attribute("Column") == "PostedAt") &&
            range.Elements(ns + "RangeExpressions").Descendants(ns + "Const")
                .Any(value => (string?)value.Attribute("ConstValue") == "NULL")) == true;

    private static bool HasPostedAtParameterResidual(XElement? predicate, XNamespace ns) =>
        predicate?.Descendants(ns + "Compare").Any(compare =>
            (string?)compare.Attribute("CompareOp") == "GE" &&
            compare.Descendants(ns + "ColumnReference").Any(column => (string?)column.Attribute("Column") == "PostedAt") &&
            compare.Descendants(ns + "ColumnReference").Any(column =>
                ((string?)column.Attribute("Column"))?.StartsWith('@') == true)) == true;

    private static string Describe(Diagnostic diagnostic) => string.Join(", ", diagnostic.Accesses.Select(access =>
        $"{access.Operation} {access.Index}; seek: {access.SeekPredicates}; residual: {access.ResidualPredicate}"));
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
    private sealed record PlanAccess(string Operation, string Index, string SeekPredicates, string ResidualPredicate,
        bool HasPostedAtRange, bool HasPostedAtParameterRange, bool HasPostedAtParameterResidual, bool HasPostedAtNotNullRange);
    private sealed record ParameterDiagnostic(string Name, string Type, byte Precision, byte Scale, object? Value);
    private sealed record PlanParameterDiagnostic(string Name, string? ParameterDataType);
    private sealed record Diagnostic(int Count, long LogicalReads, PlanAccess[] Accesses, string Sql,
        ParameterDiagnostic[] Parameters, bool HasDynamicSeek, PlanParameterDiagnostic[] PlanParameters, string[] StatisticsIo, string ActualPlan);
    private sealed record Comparison(int ColumnScale, string Mode, Diagnostic Original, Diagnostic Repaired,
        Diagnostic TypedWithoutHintWarmed, Diagnostic TypedWithoutHintCold, Diagnostic OriginalCold);
}
