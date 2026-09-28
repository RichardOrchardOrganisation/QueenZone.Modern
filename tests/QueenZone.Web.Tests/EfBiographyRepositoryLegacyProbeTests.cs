using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of <see cref="EfBiographyRepository"/> against the real legacy
/// <c>Q_BIO_LIST_SP</c> / <c>Q_BIO_DISPLAY_SP</c> procedures (#1672). Skips when
/// <c>ConnectionStrings__QueenZoneLegacy</c> is not set; the nightly <c>legacy-read-probes</c> job
/// runs it against the SQL Express mirror. Scratch-schema coverage lives in
/// <c>BiographyRepositorySqlServerTests</c>.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfBiographyRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_biography_reads_when_connection_configured()
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
        var repository = new EfBiographyRepository(dbContext);

        // smallint Q_BIO_ID and tinyint DISPLAY_SEQUENCE must materialize into the row models.
        var chapters = await repository.GetChaptersAsync();
        Assert.NotEmpty(chapters);

        var chapter = chapters[0];
        var detail = await repository.GetByIdAsync(chapter.Id);
        Assert.NotNull(detail);
        Assert.Equal(chapter.Title, detail.Title);
        Assert.False(string.IsNullOrWhiteSpace(detail.Body));

        var nav = await repository.GetAdjacentChaptersAsync(chapter.Id);
        Assert.True(nav.Previous is not null || nav.Next is not null || chapters.Count == 1);
    }
}
