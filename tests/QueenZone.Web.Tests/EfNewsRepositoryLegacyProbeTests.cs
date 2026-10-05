using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

/// <summary>
/// Read-only probe of the production <see cref="EfNewsRepository"/> archive SQL and
/// <see cref="LegacyNewsSchema"/> COL_LENGTH probes against real <c>NEWS_T</c> (#1672 / #1882).
/// Skips when <c>ConnectionStrings__QueenZoneLegacy</c> is not set; the nightly
/// <c>legacy-read-probes</c> job runs it against the SQL Express mirror. Scratch-schema
/// coverage lives in <c>NewsRepositorySqlServerTests</c>. Search stays with
/// <c>EfNewsFullTextSearchLiveProbeTests</c> / <c>EfNewsSectionLiveProbeTests</c>.
/// </summary>
[Collection(LiveDatabaseProbeCollection.Name)]
public sealed class EfNewsRepositoryLegacyProbeTests
{
    [Fact]
    public async Task Probe_legacy_news_archive_reads_when_connection_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__QueenZoneLegacy");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var columns = LegacyNewsSchema.GetNewsColumnAvailability(connectionString);
        Assert.True(columns.HasSourceUrlColumn);
        Assert.True(columns.HasSlugColumn);
        Assert.True(columns.HasCreatedAtColumn);
        Assert.True(columns.HasUpdatedAtColumn);
        Assert.True(columns.HasEditorEmailColumn);
        Assert.True(columns.HasImageBlobKeyColumn);
        Assert.True(columns.HasImageGalleryPicIdColumn);
        Assert.True(columns.HasForumTopicIdColumn);
        Assert.True(LegacyNewsSchema.HasLegacySlugColumn(connectionString));

        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        await using var dbContext = new QueenZoneDbContext(options);
        var repository = new EfNewsRepository(dbContext, new EfNewsSuggestionRepository(dbContext));

        var count = await repository.GetPublishedCountAsync();
        Assert.True(count > 0, "The live news archive should have published records.");

        var latest = await repository.GetLatestAsync(5);
        Assert.NotEmpty(latest);
        Assert.All(latest, item =>
        {
            Assert.True(item.IsPublished);
            Assert.Equal(string.Empty, item.Body);
        });

        var archivePage = await repository.GetArchivePageAsync(1, 20);
        Assert.NotEmpty(archivePage);
        Assert.All(archivePage, item => Assert.True(item.IsPublished));

        var detail = await repository.GetByIdAsync(archivePage[0].Id);
        Assert.NotNull(detail);
        Assert.Equal(archivePage[0].Id, detail.Id);
        Assert.True(detail.IsPublished);
        Assert.False(string.IsNullOrWhiteSpace(detail.Body));

        var batched = await repository.GetByIdsAsync([archivePage[0].Id, int.MaxValue]);
        Assert.Contains(batched, item => item.Id == archivePage[0].Id);

        var sitemap = await repository.GetPublishedSitemapEntriesAsync();
        Assert.NotEmpty(sitemap);
        Assert.Contains(sitemap, entry => entry.Id == detail.Id && entry.Title == detail.Title);

        var range = await repository.GetArchiveYearRangeAsync();
        Assert.NotNull(range.MinYear);
        Assert.NotNull(range.MaxYear);
        Assert.True(range.MinYear <= range.MaxYear);

        var decade = NewsArchiveFilter.Parse(latest[0].PublishedAt.Year);
        Assert.True(decade.IsActive);
        var decadeCount = await repository.GetPublishedCountAsync(decade);
        var decadePage = await repository.GetArchivePageAsync(1, 5, decade);
        Assert.True(decadeCount >= decadePage.Count);
        Assert.True(decadeCount <= count);
        Assert.NotEmpty(decadePage);
        var decadeStart = decade.DecadeStartYear!.Value;
        Assert.All(decadePage, item =>
        {
            Assert.True(item.PublishedAt.Year >= decadeStart);
            Assert.True(item.PublishedAt.Year < decadeStart + 10);
        });
    }
}
