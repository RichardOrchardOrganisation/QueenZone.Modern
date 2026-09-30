using System.Globalization;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the production <see cref="EfFreddieTributeRepository"/> SQL against a scratch
/// <c>FREDDIE_T</c> (#1672 / #1887). The table matches the <c>queenzone_legacy_sync</c>
/// read-only dump of 2026-09-29 (see <see cref="LegacyFreddieTributeSchema"/>): <c>int</c>
/// identity ids, <c>varchar</c> name/thought/date/time/country with
/// <c>SQL_Latin1_General_CP1_CI_AS</c>, and a nullable <c>tinyint</c> <c>DISPLAY</c>.
/// There are no stored procedures — the repository uses inline SQL. The read-only mirror
/// probe is <c>EfFreddieTributeRepositoryLegacyProbeTests</c> in <c>QueenZone.Web.Tests</c>.
/// </summary>
public sealed class FreddieTributeRepositorySqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneFreddieTributeTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfFreddieTributeRepository repository = null!;

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
        repository = new EfFreddieTributeRepository(dbContext);
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public async Task GetPage_and_GetVisibleById_filter_hidden_blank_and_materialize_legacy_columns()
    {
        await ClearAsync();
        await InsertAsync("Hidden", "Private note", "24 November 2001", "08:00", "UK", display: 0);
        await InsertAsync("Blank", "", "24 November 2001", "09:00", "UK", display: 1);
        await InsertAsync("Spaces", "   ", "24 November 2001", "09:30", "UK", display: 1);
        var maya = await InsertAsync("  Maya  ", "  Freddie still shines.  ", "24 November 2001", "  10:00  ",
            "  India  ", display: 1);
        var anon = await InsertAsync(null, "Anonymous love for Freddie.", "24 November 2001", null, null,
            display: 1);
        await InsertAsync("Null display", "Has a thought", "24 November 2001", "11:00", "US", display: null);

        var page = await repository.GetPageAsync(1, 10);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal([anon, maya], page.Items.Select(item => item.Id));
        Assert.Equal("Anonymous", page.Items[0].Name);
        Assert.Null(page.Items[0].Country);
        Assert.Null(page.Items[0].TimeText);
        Assert.Equal("Maya", page.Items[1].Name);
        Assert.Equal("Freddie still shines.", page.Items[1].Thought);
        Assert.Equal("India", page.Items[1].Country);
        Assert.Equal("10:00", page.Items[1].TimeText);
        Assert.Equal("24 November 2001", page.Items[1].DateText);

        var second = await repository.GetPageAsync(2, 1);
        Assert.Equal(maya, Assert.Single(second.Items).Id);
        Assert.Equal(2, second.TotalCount);

        var clamped = await repository.GetPageAsync(0, 0);
        Assert.Equal(anon, Assert.Single(clamped.Items).Id);

        var byId = await repository.GetVisibleByIdAsync(maya);
        Assert.NotNull(byId);
        Assert.Equal("Maya", byId.Name);
        Assert.Null(await repository.GetVisibleByIdAsync(1));
        Assert.Null(await repository.GetVisibleByIdAsync(2));
        Assert.Null(await repository.GetVisibleByIdAsync(404));
    }

    [Fact]
    public async Task PickRandom_returns_visible_id_or_null_when_none()
    {
        await ClearAsync();
        Assert.Null(await repository.GetRandomAsync());
        Assert.Null(await repository.PickRandomVisibleIdAsync());

        var hidden = await InsertAsync("Hidden", "Private note", "24 November 2001", "08:00", "UK", display: 0);
        Assert.Null(await repository.GetRandomAsync());

        var visible = await InsertAsync("Maya", "Freddie still shines.", "24 November 2001", "10:00", "India",
            display: 1);
        var random = await repository.GetRandomAsync();
        Assert.NotNull(random);
        Assert.Equal(visible, random.Id);
        Assert.Equal("Maya", random.Name);
        Assert.Equal(visible, await repository.PickRandomVisibleIdAsync());
        Assert.Null(await repository.GetVisibleByIdAsync(hidden));
    }

    [Fact]
    public async Task Display_null_is_hidden_from_public_reads()
    {
        await ClearAsync();
        var hidden = await InsertAsync("Null display", "Has a thought", "24 November 2001", "11:00", "US",
            display: null);
        var visible = await InsertAsync("Maya", "Freddie still shines.", "24 November 2001", "10:00", "India",
            display: 1);

        var page = await repository.GetPageAsync(1, 10);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(visible, Assert.Single(page.Items).Id);
        Assert.Null(await repository.GetVisibleByIdAsync(hidden));
        Assert.Equal(visible, await repository.PickRandomVisibleIdAsync());
    }

    [Fact]
    public async Task Freddie_Date_default_materializes_as_server_formatted_text()
    {
        await ClearAsync();
        var ids = await dbContext.Database.SqlQueryRaw<int>(
            """
            INSERT INTO dbo.FREDDIE_T (Name, Thought, DISPLAY)
            OUTPUT CAST(INSERTED.ID AS int) AS Value
            VALUES ('Maya', 'Still shining.', 1)
            """).ToListAsync();
        var id = ids.Single();

        var stored = Assert.Single(await dbContext.Database
            .SqlQueryRaw<string>("SELECT Freddie_Date AS Value FROM dbo.FREDDIE_T WHERE ID = {0}", id)
            .ToListAsync());
        var serverYear = Assert.Single(await dbContext.Database
            .SqlQueryRaw<int>("SELECT YEAR(GETDATE()) AS Value")
            .ToListAsync());
        // DF_Freddie_Date is getdate() written into varchar(50) in the server's datetime format.
        Assert.False(string.IsNullOrWhiteSpace(stored));
        Assert.Contains(
            serverYear.ToString(CultureInfo.InvariantCulture),
            stored,
            StringComparison.Ordinal);

        var tribute = await repository.GetVisibleByIdAsync(id);
        Assert.NotNull(tribute);
        Assert.Equal(stored.Trim(), tribute.DateText);
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
            name is string n ? n : DBNull.Value,
            thought is string t ? t : DBNull.Value,
            dateText,
            timeText is string tm ? tm : DBNull.Value,
            country is string c ? c : DBNull.Value,
            display is int d ? d : DBNull.Value).ToListAsync();
        return ids.Single();
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    // Creates and drops the scratch database; the legacy table comes from LegacyFreddieTributeSchema.
    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
