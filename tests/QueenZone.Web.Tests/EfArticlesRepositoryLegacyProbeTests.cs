using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfArticlesRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_article_archive_reads_when_connection_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(connectionString).Options);
        var repository = new EfArticlesRepository(dbContext, null!);

        var count = await repository.GetPublishedCountAsync();
        var latest = await repository.GetLatestAsync(5);
        var first = await repository.GetArchivePageAsync(1, 5);
        var sitemap = await repository.GetPublishedSitemapEntriesAsync();

        Assert.True(count > 0);
        Assert.NotEmpty(latest);
        Assert.Equal(latest.Select(item => item.Id), first.Select(item => item.Id));
        Assert.Equal(count, sitemap.Count);

        var detail = await repository.GetByIdAsync(latest[0].Id);
        Assert.NotNull(detail);
        Assert.Equal(latest[0].Title, detail.Title);
        Assert.NotEmpty(detail.Body);
    }
}
