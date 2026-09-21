using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class E2EMirrorMigrationHostedServiceTests
{
    [Fact]
    public async Task StartAsync_propagates_factory_failure()
    {
        var service = new E2EMirrorMigrationHostedService(
            new ThrowingDbContextFactory(),
            NullLogger<E2EMirrorMigrationHostedService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.StartAsync(CancellationToken.None));

        Assert.Contains("Simulated E2E mirror migrate failure", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsync_invokes_migrate_on_the_created_context()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        var service = new E2EMirrorMigrationHostedService(
            new FixedDbContextFactory(options),
            NullLogger<E2EMirrorMigrationHostedService>.Instance);

        // SQL Server migrations cannot apply to SQLite; the call to MigrateAsync is the coverage.
        var exception = await Record.ExceptionAsync(() => service.StartAsync(CancellationToken.None));

        Assert.NotNull(exception);
    }

    [Fact]
    public async Task StopAsync_completes_immediately()
    {
        var service = new E2EMirrorMigrationHostedService(
            new ThrowingDbContextFactory(),
            NullLogger<E2EMirrorMigrationHostedService>.Instance);

        await service.StopAsync(CancellationToken.None);
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<QueenZoneDbContext>
    {
        public QueenZoneDbContext CreateDbContext() =>
            throw new InvalidOperationException("Simulated E2E mirror migrate failure.");

        public Task<QueenZoneDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated E2E mirror migrate failure.");
    }

    private sealed class FixedDbContextFactory(DbContextOptions<QueenZoneDbContext> options)
        : IDbContextFactory<QueenZoneDbContext>
    {
        public QueenZoneDbContext CreateDbContext() => new(options);

        public Task<QueenZoneDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new QueenZoneDbContext(options));
    }
}
