using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

/// <summary>Exercises statistics DDL only in unique, disposable synthetic databases.</summary>
public sealed class ForumPostEditStatisticsSqlServerTests : IAsyncLifetime
{
    private readonly ModernForumSqlServerDatabase database = new();
    private const string StatisticName = "ST_ModernForumPost_UpdatedAt";

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        await database.SeedCategoryAsync(1, 1, "Archive", 1);
        await database.SeedThreadAsync(10, 10, 1, 1, "Synthetic archive", "Synthetic author");
        await database.SeedPostAsync(100, 100, 10, 10, 1, "Synthetic author", "Original",
            new DateTime(2000, 1, 1), authorLegacyUserId: 1);
        await database.ExecuteAsync("UPDATE dbo.ModernForumPost SET UpdatedAt = '2000-01-01' WHERE Id = 100;");
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task Creates_statistics_once_and_retains_them_on_down()
    {
        await ApplyAsync();
        var statistic = Assert.Single(await StatisticsAsync());
        Assert.Equal(StatisticName, statistic.Name);
        Assert.False(statistic.AutoCreated);
        Assert.False(statistic.HasFilter);
        Assert.NotNull(statistic.LastUpdated);
        Assert.Equal(1L, statistic.RowsSampled);
        await ApplyAsync();
        Assert.Equal(statistic, Assert.Single(await StatisticsAsync()));
        await ApplyAsync(down: true);
        Assert.Equal(statistic, Assert.Single(await StatisticsAsync()));
        await ApplyAsync();
        Assert.Equal(statistic, Assert.Single(await StatisticsAsync()));
    }

    [Theory]
    [InlineData("CREATE STATISTICS ExistingUpdatedAt ON dbo.ModernForumPost (UpdatedAt);")]
    [InlineData("CREATE STATISTICS ExistingUpdatedAt ON dbo.ModernForumPost (UpdatedAt, Id);")]
    [InlineData("CREATE INDEX ExistingUpdatedAt ON dbo.ModernForumPost (UpdatedAt);")]
    [InlineData("CREATE INDEX ExistingUpdatedAt ON dbo.ModernForumPost (UpdatedAt, Id);")]
    public async Task Reuses_existing_unfiltered_leading_histogram_without_resampling(string ddl)
    {
        await database.ExecuteAsync(ddl);
        var existing = Assert.Single(await StatisticsAsync());
        await ApplyAsync();
        Assert.Equal(existing, Assert.Single(await StatisticsAsync()));
        await ApplyAsync(down: true);
        Assert.Equal(existing, Assert.Single(await StatisticsAsync()));
    }

    [Fact]
    public async Task Reuses_automatically_created_histogram()
    {
        await using var context = database.CreateContext();
        // Force compilation of an UpdatedAt predicate on synthetic rows, without executing a write.
        await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM dbo.ModernForumPost WHERE UpdatedAt = '2000-01-01' OPTION (RECOMPILE)").ToListAsync();
        var automatic = Assert.Single(await StatisticsAsync());
        Assert.True(automatic.AutoCreated);
        await ApplyAsync();
        Assert.Equal(automatic, Assert.Single(await StatisticsAsync()));
        await ApplyAsync(down: true);
        Assert.Equal(automatic, Assert.Single(await StatisticsAsync()));
    }

    [Theory]
    [InlineData("CREATE STATISTICS OtherStatistic ON dbo.ModernForumPost (UpdatedAt) WHERE IsHidden = 0;")]
    [InlineData("CREATE STATISTICS OtherStatistic ON dbo.ModernForumPost (Id, UpdatedAt);")]
    [InlineData("CREATE INDEX OtherStatistic ON dbo.ModernForumPost (Id) INCLUDE (UpdatedAt);")]
    public async Task Filtered_nonleading_or_included_columns_do_not_replace_histogram(string ddl)
    {
        await database.ExecuteAsync(ddl);
        await ApplyAsync();
        var statistics = await StatisticsAsync();
        Assert.Contains(statistics, item => item.Name == StatisticName && !item.HasFilter && !item.AutoCreated);
        await using var context = database.CreateContext();
        Assert.Equal(1, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.stats WHERE object_id = OBJECT_ID(N'dbo.ModernForumPost') AND name = N'OtherStatistic'").SingleAsync());
    }

    [Theory]
    [InlineData("DROP TABLE dbo.ModernForumPost;")]
    [InlineData("EXEC sys.sp_rename N'dbo.ModernForumPost.UpdatedAt', N'OtherTimestamp', N'COLUMN';")]
    public async Task Missing_table_or_column_is_safe_noop(string ddl)
    {
        await database.ExecuteAsync(ddl);
        await ApplyAsync();
        await using var context = database.CreateContext();
        Assert.Equal(0, await context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.stats WHERE object_id = OBJECT_ID(N'dbo.ModernForumPost') AND name = N'ST_ModernForumPost_UpdatedAt'").SingleAsync());
    }

    [Fact]
    public async Task Name_collision_fails_without_replacing_independent_statistics()
    {
        await database.ExecuteAsync($"CREATE STATISTICS {StatisticName} ON dbo.ModernForumPost (Id);");
        var exception = await Assert.ThrowsAsync<SqlException>(() => ApplyAsync());
        Assert.Equal(1927, exception.Number);
        await using var context = database.CreateContext();
        var column = await context.Database.SqlQueryRaw<string>("""
            SELECT c.name AS Value FROM sys.stats s
            INNER JOIN sys.stats_columns sc ON sc.object_id = s.object_id AND sc.stats_id = s.stats_id
            INNER JOIN sys.columns c ON c.object_id = sc.object_id AND c.column_id = sc.column_id
            WHERE s.object_id = OBJECT_ID(N'dbo.ModernForumPost')
              AND s.name = N'ST_ModernForumPost_UpdatedAt' AND sc.stats_column_id = 1
            """).SingleAsync();
        Assert.Equal("Id", column);
    }

    [Fact]
    public async Task Archived_post_edit_uses_precreated_statistics_and_preserves_concurrency()
    {
        await ApplyMigrationAsync(new AddModernForumPostAuthorLegacyUserIndex());
        await ApplyMigrationAsync(new OrderModernForumPostArchiveAuthorIndex());
        await ApplyMigrationAsync(new AddModernForumArchiveAuthorSummary());
        await ApplyMigrationAsync(new SkipArchiveAuthorSummaryForPostEdits());
        await ApplyAsync();
        var before = Assert.Single(await StatisticsAsync());
        await using var context = database.CreateContext();
        var repository = new EfForumWriteRepository(context);
        var timestamp = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var result = await repository.UpdatePostAsync(100, Guid.Empty, "<p>Edited</p>", true, 0, timestamp);
        Assert.Equal(ForumPostUpdateStatus.Success, result.Status);
        await using var verify = database.CreateContext();
        var post = await verify.ModernForumPosts.SingleAsync(item => item.LegacyPostId == 100);
        Assert.Equal("<p>Edited</p>", post.BodyHtml);
        Assert.Equal(1, post.EditCount);
        Assert.NotNull(post.EditedAt);
        Assert.True(post.UpdatedAt > timestamp.UtcDateTime);
        Assert.Equal(1, post.AuthorLegacyUserId);
        Assert.Null(post.AuthorMemberId);
        Assert.Equal("Synthetic author", post.AuthorDisplayName);
        var after = Assert.Single(await StatisticsAsync());
        Assert.Equal(before, after);
        await using var stale = database.CreateContext();
        var conflict = await new EfForumWriteRepository(stale).UpdatePostAsync(100, Guid.Empty, "Stale", true, 0, timestamp);
        Assert.Equal(ForumPostUpdateStatus.ConcurrencyConflict, conflict.Status);
        Assert.Equal("<p>Edited</p>", await verify.ModernForumPosts.AsNoTracking().Where(item => item.LegacyPostId == 100).Select(item => item.BodyHtml).SingleAsync());
    }

    private Task ApplyAsync(bool down = false) => ApplyMigrationAsync(
        new PrecreateForumPostUpdatedAtStatistics { ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer" }, down);

    private async Task ApplyMigrationAsync(Migration migration, bool down = false)
    {
        foreach (var operation in (down ? migration.DownOperations : migration.UpOperations).OfType<SqlOperation>())
        {
            await database.ExecuteAsync(operation.Sql);
        }
    }

    private async Task<List<Statistic>> StatisticsAsync()
    {
        await using var context = database.CreateContext();
        return await context.Database.SqlQueryRaw<Statistic>("""
            SELECT s.stats_id AS Id, s.name AS Name, s.auto_created AS AutoCreated,
                   s.has_filter AS HasFilter, STATS_DATE(s.object_id, s.stats_id) AS LastUpdated,
                   p.rows_sampled AS RowsSampled
            FROM sys.stats s
            INNER JOIN sys.stats_columns sc ON sc.object_id = s.object_id AND sc.stats_id = s.stats_id
            INNER JOIN sys.columns c ON c.object_id = sc.object_id AND c.column_id = sc.column_id
            OUTER APPLY sys.dm_db_stats_properties(s.object_id, s.stats_id) p
            WHERE s.object_id = OBJECT_ID(N'dbo.ModernForumPost')
                AND sc.stats_column_id = 1 AND c.name = N'UpdatedAt'
            ORDER BY s.stats_id
            """).ToListAsync();
    }

    private sealed record Statistic(int Id, string Name, bool AutoCreated, bool HasFilter, DateTime? LastUpdated, long? RowsSampled);
}
