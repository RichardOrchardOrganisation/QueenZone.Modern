using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of <see cref="EfLinksRepository"/> against the real legacy
/// <c>Q_LINK_CAT_T</c> / <c>QUEEN_FEATURED_SITE_T</c> join and modern
/// <c>QueenLinkChecks</c> (#1672 / #1889). Skips when
/// <c>ConnectionStrings__QueenZoneLegacy</c> is not set; the nightly
/// <c>legacy-read-probes</c> job runs it against the SQL Express mirror.
/// Scratch-schema coverage lives in <c>LinksRepositorySqlServerTests</c>
/// (verified against the 2026-09-29 <c>queenzone_legacy_sync</c> catalog dump).
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfLinksRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_links_reads_when_connection_configured()
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
        var repository = new EfLinksRepository(dbContext);

        // smallint category ids and tinyint site category / flag columns must
        // materialize through the CAST projections; QueenLinkChecks is present.
        var categories = await repository.GetCategoriesWithLinksAsync();
        Assert.NotEmpty(categories);
        Assert.All(categories, category =>
        {
            Assert.True(category.Id > 0);
            Assert.False(string.IsNullOrWhiteSpace(category.Name));
            Assert.NotEmpty(category.Links);
        });

        var first = categories[0].Links[0];
        Assert.False(string.IsNullOrWhiteSpace(first.Title));
        Assert.StartsWith("http", first.Url, StringComparison.OrdinalIgnoreCase);

        var validation = await repository.GetLinksForValidationAsync();
        Assert.NotEmpty(validation);
        Assert.True(validation.Count >= categories.Sum(category => category.Links.Count));
        Assert.Contains(validation, item => item.Link.Id == first.Id);

        Assert.All(validation, item => Assert.True(item.ConsecutiveFailureCount >= 0));
    }
}
