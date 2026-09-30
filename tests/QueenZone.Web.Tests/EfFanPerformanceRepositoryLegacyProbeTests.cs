using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of the production <see cref="EfFanPerformanceRepository"/> SQL against
/// the real legacy <c>Q_STAGE_T</c> table (#1672 / #1888). Skips when
/// <c>ConnectionStrings__QueenZoneLegacy</c> is not set; the nightly
/// <c>legacy-read-probes</c> job runs it against the SQL Express mirror.
/// Scratch-schema coverage lives in <c>FanPerformanceRepositorySqlServerTests</c>.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfFanPerformanceRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_fan_performance_reads_when_connection_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(options);
        var repository = new EfFanPerformanceRepository(dbContext);

        // smallint Q_STAGE_ID and tinyint DISPLAY must materialize through DISPLAY = 1.
        var count = await repository.GetVisibleCountAsync();
        var page = await repository.GetPageAsync(1, 5);
        Assert.True(count >= page.Count);
        if (count <= 5)
        {
            Assert.Equal(count, page.Count);
        }

        Assert.NotEmpty(page);

        var newest = page[0];
        var loaded = await repository.GetByIdAsync(newest.Id);
        Assert.NotNull(loaded);
        Assert.Equal(newest.Id, loaded.Id);
        Assert.Equal(newest.Title, loaded.Title);
        Assert.Equal(newest.AudioFileName, loaded.AudioFileName);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Title));

        if (page.Count >= 2)
        {
            Assert.True(
                page[0].DateAdded > page[1].DateAdded
                || (page[0].DateAdded == page[1].DateAdded && page[0].Id > page[1].Id));
        }

        Assert.Null(await repository.GetByIdAsync(int.MaxValue));
    }
}
