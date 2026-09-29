using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of <see cref="EfFreddieTributeRepository"/> against the real legacy
/// <c>FREDDIE_T</c> table (#1672 / #1887). Skips when <c>ConnectionStrings__QueenZoneLegacy</c>
/// is not set; the nightly <c>legacy-read-probes</c> job runs it against the SQL Express mirror.
/// Scratch-schema coverage lives in <c>FreddieTributeRepositorySqlServerTests</c>.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfFreddieTributeRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_freddie_tribute_reads_when_connection_configured()
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
        var repository = new EfFreddieTributeRepository(dbContext);

        // int ID and tinyint DISPLAY must materialize through the production SQL (DISPLAY = 1).
        var page = await repository.GetPageAsync(1, 5);
        Assert.True(page.TotalCount >= page.Items.Count);
        Assert.NotEmpty(page.Items);

        var tribute = page.Items[0];
        var loaded = await repository.GetVisibleByIdAsync(tribute.Id);
        Assert.NotNull(loaded);
        Assert.Equal(tribute.Id, loaded.Id);
        Assert.Equal(tribute.Thought, loaded.Thought);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Name));

        var picked = await repository.PickRandomVisibleIdAsync();
        Assert.NotNull(picked);
        Assert.NotNull(await repository.GetVisibleByIdAsync(picked.Value));
    }
}
