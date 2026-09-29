using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfAdminFreddieTributeRepository"/> against a scratch
/// <c>FREDDIE_T</c> (#1672 / #1887). Column types come from <c>docs/db-schema.txt</c> (the
/// committed legacy dump; this environment cannot reach <c>queenzone_legacy_sync</c>), including
/// the nullable <c>tinyint</c> <c>DISPLAY</c> that admin maps through
/// <c>CASE WHEN DISPLAY = 1 THEN CAST(1 AS bit)</c>. Covers list filters, duplicate counts,
/// visibility/delete writes, and compare-and-swap concurrency. The modern repository does not
/// call the legacy <c>Q_FREDDIE_*</c> procedures.
/// </summary>
public sealed class AdminFreddieTributeRepositorySqlServerTests : IAsyncLifetime
{
    private const string Editor = "editor@example.com";

    private readonly string databaseName = $"QueenZoneAdminFreddieTributeTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfAdminFreddieTributeRepository repository = null!;

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
            await schema.Database.ExecuteSqlRawAsync(LegacyFreddieTributeSchema.CreateTableSql);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new EfAdminFreddieTributeRepository(dbContext);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task GetPage_filters_orders_pages_and_counts_duplicates()
    {
        await ClearAsync();
        var firstDup = await InsertAsync("Visible", "Repeated", "24 November 2001", "08:00", "UK", display: 1);
        var secondDup = await InsertAsync("visible", "repeated", "24 November 2001", "08:01", "UK", display: 1);
        var hidden = await InsertAsync("Hidden", "Filtered", "24 November 2001", null, "US", display: 0);
        var india = await InsertAsync("  Maya  ", "  Freddie still shines.  ", "24 November 2001", "  10:00  ",
            "India", display: 1);
        var nullDisplay = await InsertAsync("Null display", "Has a thought", "24 November 2001", "11:00", "CA",
            display: null);

        var all = await repository.GetPageAsync(new AdminFreddieTributeListFilter(null, null, false), 1, 10);
        Assert.Equal(5, all.TotalCount);
        Assert.Equal([nullDisplay, india, hidden, secondDup, firstDup], all.Items.Select(item => item.Id));
        Assert.Equal(1, all.Page);
        Assert.Equal(10, all.PageSize);

        var indiaItem = all.Items[1];
        Assert.Equal("Maya", indiaItem.Name);
        Assert.Equal("Freddie still shines.", indiaItem.Thought);
        Assert.Equal("10:00", indiaItem.TimeText);
        Assert.True(indiaItem.IsVisible);
        Assert.Equal(1, indiaItem.DuplicateCount);

        Assert.False(all.Items[0].IsVisible);

        var second = await repository.GetPageAsync(new AdminFreddieTributeListFilter(null, null, false), 2, 1);
        Assert.Equal(5, second.TotalCount);
        Assert.Equal(india, Assert.Single(second.Items).Id);

        var visible = await repository.GetPageAsync(new AdminFreddieTributeListFilter(true, null, false), 1, 10);
        Assert.Equal(3, visible.TotalCount);
        Assert.All(visible.Items, item => Assert.True(item.IsVisible));

        var hiddenPage = await repository.GetPageAsync(new AdminFreddieTributeListFilter(false, null, false), 1, 10);
        Assert.Equal([hidden], hiddenPage.Items.Select(item => item.Id));
        Assert.False(hiddenPage.Items[0].IsVisible);
        Assert.Null(hiddenPage.Items[0].TimeText);

        var byThought = await repository.GetPageAsync(
            new AdminFreddieTributeListFilter(null, " Repeated ", false), 1, 10);
        Assert.Equal([secondDup, firstDup], byThought.Items.Select(item => item.Id));
        var byCountry = await repository.GetPageAsync(
            new AdminFreddieTributeListFilter(null, "India", false), 1, 10);
        Assert.Equal(india, Assert.Single(byCountry.Items).Id);

        var duplicates = await repository.GetPageAsync(
            new AdminFreddieTributeListFilter(true, "Repeated", DuplicatesOnly: true), 1, 10);
        Assert.Equal(2, duplicates.TotalCount);
        Assert.Equal([secondDup, firstDup], duplicates.Items.Select(item => item.Id));
        Assert.All(duplicates.Items, item => Assert.Equal(2, item.DuplicateCount));

        var clamped = await repository.GetPageAsync(new AdminFreddieTributeListFilter(null, null, false), 0, 0);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(1, clamped.PageSize);

        var loaded = await repository.GetByIdAsync(hidden);
        Assert.NotNull(loaded);
        Assert.False(loaded.IsVisible);
        Assert.Null(loaded.TimeText);
        Assert.Null(await repository.GetByIdAsync(404));
    }

    [Fact]
    public async Task Writes_round_trip_enforce_concurrency()
    {
        await ClearAsync();
        var id = await InsertAsync("  Live  ", "Keep the music playing.", "24 November 2001", "09:00", "UK",
            display: 1);

        var created = await repository.GetByIdAsync(id);
        Assert.NotNull(created);
        Assert.Equal("Live", created.Name);
        Assert.True(created.IsVisible);

        await repository.SetVisibilityAsync(id, false, Editor, expectedIsVisible: true);
        Assert.False((await repository.GetByIdAsync(id))!.IsVisible);

        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            repository.SetVisibilityAsync(id, true, Editor, expectedIsVisible: true));
        await repository.SetVisibilityAsync(id, true, Editor, expectedIsVisible: false);
        Assert.True((await repository.GetByIdAsync(id))!.IsVisible);

        await Assert.ThrowsAsync<OptimisticConcurrencyException>(() =>
            repository.DeleteAsync(id, Editor, expectedIsVisible: false));
        await repository.DeleteAsync(id, Editor, expectedIsVisible: true);
        Assert.Null(await repository.GetByIdAsync(id));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.SetVisibilityAsync(404, false, Editor));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.DeleteAsync(id, Editor));
    }

    private Task ClearAsync() =>
        dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM dbo.FREDDIE_T; DBCC CHECKIDENT ('dbo.FREDDIE_T', RESEED, 0);");

    private async Task<int> InsertAsync(
        string? name,
        string? thought,
        string dateText,
        string? timeText,
        string? country,
        int? display)
    {
        var ids = await dbContext.Database.SqlQueryRaw<int>(
            """
            INSERT INTO dbo.FREDDIE_T (Name, Thought, Freddie_Date, Freddie_Time, Country, DISPLAY)
            OUTPUT CAST(INSERTED.ID AS int) AS Value
            VALUES ({0}, {1}, {2}, {3}, {4}, {5})
            """,
            name, thought, dateText, timeText, country, display).ToListAsync();
        return ids.Single();
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; the legacy table comes from LegacyFreddieTributeSchema.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
