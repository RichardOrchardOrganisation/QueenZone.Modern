using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs the shared <see cref="EfRunLeaseServiceBase{TLease,TEntity}"/> raw SQL for both lease tables
/// against SQL Server (#1762), covering acquire, contention, release and expired takeover.
/// </summary>
public sealed class RunLeaseServiceSqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneRunLeaseTests_{Guid.NewGuid():N}";

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            return new SqlConnectionStringBuilder(source) { InitialCatalog = databaseName }.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using (var master = new SqlConnection(new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{databaseName}]";
            await create.ExecuteNonQueryAsync();
        }

        await using var context = NewContext();
        foreach (var table in new[] { "NewsAgentRunLeases", "SearchReindexLeases" })
        {
            // Mirrors the lease entity configurations (LeaseEntityConfigurationBase).
            await context.Database.ExecuteSqlRawAsync(
                $"CREATE TABLE {table} (LeaseName nvarchar(100) NOT NULL PRIMARY KEY, HolderId nvarchar(64) NOT NULL, AcquiredAtUtc datetime2 NOT NULL, ExpiresAtUtc datetime2 NOT NULL)");
        }
    }

    public async Task DisposeAsync()
    {
        await using var context = NewContext();
        await context.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task NewsAgentLease_IsExclusiveUntilReleased()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = new EfNewsAgentRunLeaseService(first);
        var b = new EfNewsAgentRunLeaseService(second);

        var lease = await a.TryAcquireAsync("gather", TimeSpan.FromMinutes(5));
        Assert.NotNull(lease);
        Assert.Null(await b.TryAcquireAsync("gather", TimeSpan.FromMinutes(5)));

        await lease.DisposeAsync();
        var reacquired = await b.TryAcquireAsync("gather", TimeSpan.FromMinutes(5));
        Assert.NotNull(reacquired);
        await reacquired.DisposeAsync();
    }

    [Fact]
    public async Task SearchReindexLease_IsExclusiveUntilReleased()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = new EfSearchReindexRunLeaseService(first);
        var b = new EfSearchReindexRunLeaseService(second);

        var lease = await a.TryAcquireAsync("reindex", TimeSpan.FromMinutes(5));
        Assert.NotNull(lease);
        Assert.Null(await b.TryAcquireAsync("reindex", TimeSpan.FromMinutes(5)));

        await lease.DisposeAsync();
        var reacquired = await b.TryAcquireAsync("reindex", TimeSpan.FromMinutes(5));
        Assert.NotNull(reacquired);
        await reacquired.DisposeAsync();
    }

    [Fact]
    public async Task ExpiredLease_CanBeTakenOver()
    {
        await using var first = NewContext();
        await using var second = NewContext();
        var a = new EfSearchReindexRunLeaseService(first);
        var b = new EfSearchReindexRunLeaseService(second);

        Assert.NotNull(await a.TryAcquireAsync("reindex", TimeSpan.FromSeconds(-1)));
        Assert.NotNull(await b.TryAcquireAsync("reindex", TimeSpan.FromMinutes(5)));
    }

    private QueenZoneDbContext NewContext() =>
        new(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlServer(ConnectionString).Options);
}
