using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of <see cref="EfAdminFanPerformanceRepository"/> against the real
/// legacy <c>Q_STAGE_T</c> table (#1672 / #1888). Skips when
/// <c>ConnectionStrings__QueenZoneLegacy</c> is not set; the nightly
/// <c>legacy-read-probes</c> job runs it against the SQL Express mirror.
/// Scratch-schema coverage lives in <c>AdminFanPerformanceRepositorySqlServerTests</c>.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfAdminFanPerformanceRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_admin_fan_performance_reads_when_connection_configured()
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
        var repository = new EfAdminFanPerformanceRepository(dbContext);

        // smallint Q_STAGE_ID, varchar THESIZE, and tinyint DISPLAY must materialize.
        var page = await repository.GetPageAsync(new AdminFanPerformanceListFilter(), 1, 5);
        Assert.NotEmpty(page.Items);
        Assert.True(page.TotalCount >= page.Items.Count);

        var item = page.Items[0];
        var loaded = await repository.GetByIdAsync(item.Id);
        Assert.Equal(item, loaded);

        var visible = await repository.GetPageAsync(new AdminFanPerformanceListFilter(IsVisible: true), 1, 5);
        Assert.All(visible.Items, row => Assert.True(row.IsVisible));
        Assert.True(visible.TotalCount <= page.TotalCount);

        var search = item.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (search is not null)
        {
            var searched = await repository.GetPageAsync(new AdminFanPerformanceListFilter(Search: search), 1, 1);
            Assert.True(searched.TotalCount >= 1);
        }

        Assert.Null(await repository.GetByIdAsync(int.MaxValue));
    }
}
