using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

public sealed class NewsColumnAvailabilityWarmupServiceTests
{
    [Fact]
    public async Task StartAsync_swallows_probe_failure_and_does_not_block_startup()
    {
        var service = new NewsColumnAvailabilityWarmupService(
            new ThrowingDbContextFactory(),
            NullLogger<NewsColumnAvailabilityWarmupService>.Instance);

        // A failed startup probe is a degraded path, not a boot failure (#1161).
        Assert.Null(await Record.ExceptionAsync(() => service.StartAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task StartAsync_populates_cache_when_probe_already_warm()
    {
        const string connectionString = "Data Source=NewsColumnAvailabilityWarmupServiceTests-Success;Mode=Memory;Cache=Shared";
        LegacyNewsSchema.ClearColumnAvailabilityCacheForTests();
        LegacyNewsSchema.SeedColumnAvailabilityCacheForTests(
            connectionString,
            new LegacyNewsSchema.NewsColumnAvailability { HasSlugColumn = true });

        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connectionString)
            .Options;
        var service = new NewsColumnAvailabilityWarmupService(
            new FakeDbContextFactory(options),
            NullLogger<NewsColumnAvailabilityWarmupService>.Instance);

        // The cache is already warm (seeded above), so this must complete without attempting a
        // real SQL Server probe against the SQLite-backed context.
        await service.StartAsync(CancellationToken.None);

        Assert.True(LegacyNewsSchema.GetNewsColumnAvailability(connectionString).HasSlugColumn);
    }

    [Fact]
    public void StopAsync_completes_immediately()
    {
        var service = new NewsColumnAvailabilityWarmupService(
            new ThrowingDbContextFactory(),
            NullLogger<NewsColumnAvailabilityWarmupService>.Instance);

        Assert.True(service.StopAsync(CancellationToken.None).IsCompletedSuccessfully);
    }

    [Fact]
    public async Task StartAsync_is_registered_by_AddQueenZoneLegacyData()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQueenZoneLegacyData("Server=(local);Database=QueenZone;Trusted_Connection=True;");

        using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();

        Assert.Contains(hostedServices, s => s is NewsColumnAvailabilityWarmupService);
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<QueenZoneDbContext>
    {
        public QueenZoneDbContext CreateDbContext() =>
            throw new InvalidOperationException("Simulated startup probe failure.");

        public Task<QueenZoneDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated startup probe failure.");
    }

    private sealed class FakeDbContextFactory(DbContextOptions<QueenZoneDbContext> options)
        : IDbContextFactory<QueenZoneDbContext>
    {
        public QueenZoneDbContext CreateDbContext() => new(options);

        public Task<QueenZoneDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new QueenZoneDbContext(options));
    }
}
