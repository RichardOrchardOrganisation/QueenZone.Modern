using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

public sealed class FanPerformanceRepositoryContractTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneSubmissionContracts_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private EfFanPerformanceRepository repository = null!;

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
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }


    [Fact]
    public async Task ProductionReads_PageByDateThenId_AndExcludeHiddenRows()
    {
        var now = new DateTime(2026, 9, 1, 12, 0, 0);
        IAdminFanPerformanceRepository admin = new EfAdminFanPerformanceRepository(dbContext);
        var older = await admin.CreateAsync(new("Older", "Band", "Cover", "older.mp3", 1024, now.AddDays(-1), true, 120), "editor");
        var first = await admin.CreateAsync(new("First", "Band", "Cover", "first.mp3", 2048, now, true, 180), "editor");
        var second = await admin.CreateAsync(new("Second", "Band", "Cover", "second.mp3", 4096, now, true, 240), "editor");
        var hidden = await admin.CreateAsync(new("Hidden", "Band", "Cover", "hidden.mp3", 1024, now.AddDays(1), false), "editor");
        Assert.Equal(new[] { second, first }, (await repository.GetPageAsync(1, 2)).Select(row => row.Id));
        Assert.Equal(older, Assert.Single(await repository.GetPageAsync(2, 2)).Id);
        Assert.Empty(await repository.GetPageAsync(3, 2));
        Assert.Equal(3, await repository.GetVisibleCountAsync());
        Assert.Null(await repository.GetByIdAsync(hidden));
        Assert.Null(await repository.GetByIdAsync(404));
        var row = (await repository.GetByIdAsync(second))!;
        Assert.Equal("Second", row.Title);
        Assert.Equal(4096, row.FileSizeBytes);
        Assert.Equal(240, row.DurationSeconds);
        Assert.Equal(now, row.DateAdded);
        await admin.SetVisibilityAsync(second, false, "editor", true);
        Assert.Null(await repository.GetByIdAsync(second));
        Assert.Equal(2, await repository.GetVisibleCountAsync());
        Assert.Equal(new[] { first, older }, (await repository.GetPageAsync(1, 10)).Select(item => item.Id));
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
