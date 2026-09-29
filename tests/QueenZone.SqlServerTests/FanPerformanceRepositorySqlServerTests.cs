using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfFanPerformanceRepository"/> constructor
/// (<see cref="EfProductionSql.CreateFanPerformanceQueries"/>, <c>UseProcs = true</c>)
/// against a scratch <c>Q_STAGE_T</c> (#1672 / #1888). The table matches the
/// <c>queenzone_legacy_sync</c> read-only dump of 2026-09-29 (see
/// <see cref="LegacyFanPerformanceSchema"/>): <c>smallint</c> identity ids,
/// <c>varchar</c> title/performer/description/url/<c>THESIZE</c>/contact with
/// <c>SQL_Latin1_General_CP1_CI_AS</c>, <c>smalldatetime</c> <c>DATE_ADDED</c>,
/// and a nullable <c>tinyint</c> <c>DISPLAY</c> defaulting to 0. Public reads
/// filter <c>DISPLAY = 1</c> and page with <c>OFFSET/FETCH</c>. There are no
/// stored procedures — the repository uses inline SQL. The read-only mirror
/// probe is <c>EfFanPerformanceRepositoryLegacyProbeTests</c> in
/// <c>QueenZone.Web.Tests</c>.
/// </summary>
public sealed class FanPerformanceRepositorySqlServerTests : IAsyncLifetime
{
    private static readonly DateTime BaseTime = new(2026, 8, 17, 10, 0, 0);

    private readonly string databaseName = $"QueenZoneFanPerformanceTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfFanPerformanceRepository repository = null!;

    private int noSize;
    private int older;
    private int tieLow;
    private int hidden;
    private int tieHigh;
    private int nullDisplay;

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source)
            {
                InitialCatalog = databaseName,
            };
            return builder.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using (var schema = new EmptySchemaContext(SchemaOptions()))
        {
            await schema.Database.EnsureCreatedAsync();
            await schema.Database.ExecuteSqlRawAsync(LegacyFanPerformanceSchema.CreateTableSql);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfFanPerformanceRepository(dbContext);

        // Insert oldest-first so Q_STAGE_ID is not the same as DATE_ADDED order.
        noSize = await InsertAsync("Unsized", "Unknown Band", null, "  unsized.mp3  ", "not-a-size",
            BaseTime.AddDays(-2), display: 1);
        older = await InsertAsync("Liar", "Fan Band", "A cover.", "liar.mp3", "2048",
            BaseTime.AddDays(-1), display: 1, durationSeconds: 180);
        tieLow = await InsertAsync("Red Special", "Brian", "Guitar.", "red.mp3", "1024",
            BaseTime, display: 1);
        hidden = await InsertAsync("Hidden", "Private", "Not public.", "hidden.mp3", "99",
            BaseTime.AddDays(1), display: 0);
        tieHigh = await InsertAsync("Wembley", "Live Band", "Stadium.", "wembley.mp3", "5120835",
            BaseTime, display: 1);
        nullDisplay = await InsertAsync("Null display", "Ghost", "Hidden by null.", "null.mp3", "10",
            BaseTime.AddDays(2), display: null);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task GetPage_and_count_filter_hidden_and_page_with_offset_fetch()
    {
        Assert.Equal(4, await repository.GetVisibleCountAsync());

        var first = await repository.GetPageAsync(1, 2);
        Assert.Equal([tieHigh, tieLow], first.Select(item => item.Id));
        Assert.Equal("Wembley", first[0].Title);
        Assert.Equal("Live Band", first[0].PerformedBy);
        Assert.Equal("Stadium.", first[0].Description);
        Assert.Equal("wembley.mp3", first[0].AudioFileName);
        Assert.Equal(5_120_835, first[0].FileSizeBytes);
        Assert.Equal(BaseTime, first[0].DateAdded);
        Assert.Null(first[0].DurationSeconds);

        var second = await repository.GetPageAsync(2, 2);
        Assert.Equal([older, noSize], second.Select(item => item.Id));
        Assert.Equal(180, second[0].DurationSeconds);
        Assert.Equal(2048, second[0].FileSizeBytes);
        Assert.Equal(string.Empty, second[1].Description);
        Assert.Equal("unsized.mp3", second[1].AudioFileName);
        Assert.Equal(0, second[1].FileSizeBytes);

        // page <= 0 uses the same offset as page 1; hidden and null DISPLAY stay out.
        var clamped = await repository.GetPageAsync(0, 2);
        Assert.Equal([tieHigh, tieLow], clamped.Select(item => item.Id));
        Assert.DoesNotContain(clamped, item => item.Id == hidden || item.Id == nullDisplay);
    }

    [Fact]
    public async Task GetById_returns_visible_rows_and_skips_hidden()
    {
        var visible = await repository.GetByIdAsync(tieHigh);
        Assert.NotNull(visible);
        Assert.Equal("Wembley", visible.Title);
        Assert.Equal(5_120_835, visible.FileSizeBytes);

        var trimmed = await repository.GetByIdAsync(noSize);
        Assert.NotNull(trimmed);
        Assert.Equal("unsized.mp3", trimmed.AudioFileName);
        Assert.Equal(0, trimmed.FileSizeBytes);

        Assert.Null(await repository.GetByIdAsync(hidden));
        Assert.Null(await repository.GetByIdAsync(nullDisplay));
        Assert.Null(await repository.GetByIdAsync(404));
    }

    private async Task<int> InsertAsync(
        string title,
        string performedBy,
        string? description,
        string url,
        string theSize,
        DateTime dateAdded,
        int? display,
        int? durationSeconds = null)
    {
        var ids = await dbContext.Database.SqlQueryRaw<int>(
            """
            INSERT INTO dbo.Q_STAGE_T
                (TITLE, PERFORMED_BY, DESCRIPTION, URL, THESIZE, DATE_ADDED, DISPLAY, DurationSeconds)
            OUTPUT CAST(INSERTED.Q_STAGE_ID AS int) AS Value
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7})
            """,
            title,
            performedBy,
            description is string text ? text : DBNull.Value,
            url,
            theSize,
            dateAdded,
            display is int flag ? flag : DBNull.Value,
            durationSeconds is int seconds ? seconds : DBNull.Value).ToListAsync();
        return ids.Single();
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; the legacy table comes from LegacyFanPerformanceSchema.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
