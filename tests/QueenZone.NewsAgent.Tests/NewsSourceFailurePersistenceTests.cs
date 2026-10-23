using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.NewsAgent.Tests;

public sealed class NewsSourceFailurePersistenceTests
{
    [Fact]
    public async Task Failure_count_survives_new_context_and_success_resets_it()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options;
        int sourceId;
        await using (var context = new QueenZoneDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            var repository = new EfNewsDiscoveryRepository(context);
            sourceId = await repository.UpsertSourceAsync(new NewsDiscoverySourceDraft(
                "queen", "Queen", "https://example.com", "https://example.com/feed",
                NewsDiscoverySourceType.Rss, NewsDiscoveryTrustTier.Primary, 60, true, null));
            Assert.Equal(1, await repository.MarkSourceFailedAsync(sourceId));
            Assert.Equal(2, await repository.MarkSourceFailedAsync(sourceId));
        }
        await using (var context = new QueenZoneDbContext(options))
        {
            var repository = new EfNewsDiscoveryRepository(context);
            Assert.Equal(3, await repository.MarkSourceFailedAsync(sourceId));
            await repository.MarkSourceFetchedAsync(sourceId, DateTime.UtcNow);
        }
        await using (var context = new QueenZoneDbContext(options))
        {
            Assert.Equal(0, (await context.NewsDiscoverySources.SingleAsync()).ConsecutiveFailureCount);
            var repository = new EfNewsDiscoveryRepository(context);
            Assert.Equal(1, await repository.MarkSourceFailedAsync(sourceId));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.MarkSourceFailedAsync(sourceId + 1));
        }
    }

    [Fact]
    public void Failure_counter_migration_matches_sql_server_model()
    {
        using var context = new QueenZoneDbContextFactory().CreateDbContext([]);
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
