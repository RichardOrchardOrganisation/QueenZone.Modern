using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Covers SQL Server read-stat maintenance on <see cref="EfForumWriteRepository"/>
/// through public <c>CreateThreadAsync</c> / <c>CreatePostAsync</c> (#1892).
/// </summary>
public sealed class EfForumWriteReadStatsSqlServerTests : IAsyncLifetime
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AuthorId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private readonly ModernForumSqlServerDatabase database = new();
    private QueenZoneDbContext dbContext = null!;
    private EfForumWriteRepository repository = null!;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        dbContext = database.CreateContext(enableRetry: true);
        repository = new EfForumWriteRepository(dbContext);
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task CreateThread_initializes_thread_stats_and_increments_aggregates()
    {
        await SeedWritableCategoryAsync();
        await database.SeedCategoryStatsAsync(1, 10, 4, 3);
        await database.SeedArchiveStatsAsync(8, 6);
        await database.InstallSequencesAsync();

        var created = await repository.CreateThreadAsync(NewThread("A brand new topic"));

        var thread = await dbContext.ModernForumThreads.SingleAsync(item => item.LegacyTopicId == created.TopicId);
        Assert.Equal(1, await ScalarIntAsync($"SELECT PostCount FROM dbo.ModernForumThreadReadStats WHERE ThreadId = {thread.Id}"));
        Assert.Equal(created.TopicId, await ScalarIntAsync($"SELECT LegacyTopicId FROM dbo.ModernForumThreadReadStats WHERE ThreadId = {thread.Id}"));
        Assert.Equal(CreatedAt.UtcDateTime, await ScalarDateAsync($"SELECT UpdatedAt FROM dbo.ModernForumThreadReadStats WHERE ThreadId = {thread.Id}"));
        Assert.Equal((5, 4), await CategoryStatsAsync(1));
        Assert.Equal((9, 7), await ArchiveStatsAsync());
    }

    [Fact]
    public async Task CreateThread_blank_title_does_not_increment_sitemap_count()
    {
        await SeedWritableCategoryAsync();
        await database.SeedCategoryStatsAsync(1, 10, 0, 0);
        await database.SeedArchiveStatsAsync(0, 0);
        await database.InstallSequencesAsync();

        await repository.CreateThreadAsync(NewThread("   "));

        Assert.Equal((1, 0), await ArchiveStatsAsync());
        Assert.Equal((1, 1), await CategoryStatsAsync(1));
    }

    [Fact]
    public async Task CreateThread_tolerates_missing_stats_tables_or_aggregate_rows()
    {
        await SeedWritableCategoryAsync();
        await database.InstallSequencesAsync();

        var withoutRows = await repository.CreateThreadAsync(NewThread("No aggregate rows"));
        Assert.True(withoutRows.TopicId > 0);
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM dbo.ModernForumCategoryReadStats"));
        Assert.Equal(0, await ScalarIntAsync("SELECT COUNT(*) FROM dbo.ModernForumArchiveReadStats"));
        Assert.Equal(1, await ScalarIntAsync("SELECT COUNT(*) FROM dbo.ModernForumThreadReadStats"));

        await database.ExecuteAsync("""
            DROP TABLE dbo.ModernForumThreadReadStats;
            DROP TABLE dbo.ModernForumCategoryReadStats;
            DROP TABLE dbo.ModernForumArchiveReadStats;
            """);

        var withoutTables = await repository.CreateThreadAsync(NewThread("No stats tables"));
        Assert.True(withoutTables.TopicId > withoutRows.TopicId);
        Assert.Equal(2, await dbContext.ModernForumThreads.CountAsync());
    }

    [Fact]
    public async Task CreatePost_increments_existing_thread_stats()
    {
        await SeedWritableCategoryAsync();
        await database.SeedCategoryStatsAsync(1, 10, 1, 1);
        await database.SeedArchiveStatsAsync(1, 1);
        await database.InstallSequencesAsync();

        var created = await repository.CreateThreadAsync(NewThread("Starter"));
        var afterThread = await ArchiveStatsAsync();
        var thread = await dbContext.ModernForumThreads.SingleAsync(item => item.LegacyTopicId == created.TopicId);

        await repository.CreatePostAsync(new NewForumPost(
            created.TopicId, AuthorId, "Roger", "<p>Reply</p>", CreatedAt.AddMinutes(1)));

        Assert.Equal(2, await ScalarIntAsync($"SELECT PostCount FROM dbo.ModernForumThreadReadStats WHERE ThreadId = {thread.Id}"));
        Assert.Equal(CreatedAt.AddMinutes(1).UtcDateTime, await ScalarDateAsync(
            $"SELECT UpdatedAt FROM dbo.ModernForumThreadReadStats WHERE ThreadId = {thread.Id}"));
        Assert.Equal((2, 2), await CategoryStatsAsync(1));
        Assert.Equal(afterThread, await ArchiveStatsAsync());
    }

    [Fact]
    public async Task CreatePost_reconstructs_missing_thread_stats()
    {
        await SeedWritableCategoryAsync();
        await database.InstallSequencesAsync();
        var created = await repository.CreateThreadAsync(NewThread("Starter"));
        var thread = await dbContext.ModernForumThreads.SingleAsync(item => item.LegacyTopicId == created.TopicId);
        await database.ExecuteAsync($"DELETE FROM dbo.ModernForumThreadReadStats WHERE ThreadId = {thread.Id};");

        await repository.CreatePostAsync(new NewForumPost(
            created.TopicId, AuthorId, "Roger", "<p>Reply</p>", CreatedAt.AddMinutes(1)));

        Assert.Equal(2, await ScalarIntAsync($"SELECT PostCount FROM dbo.ModernForumThreadReadStats WHERE ThreadId = {thread.Id}"));
        Assert.Equal(created.TopicId, await ScalarIntAsync(
            $"SELECT LegacyTopicId FROM dbo.ModernForumThreadReadStats WHERE ThreadId = {thread.Id}"));
    }

    [Fact]
    public async Task CreatePost_reconstructs_visible_post_count_when_hidden_posts_exist()
    {
        await SeedWritableCategoryAsync();
        await database.InstallSequencesAsync();
        var created = await repository.CreateThreadAsync(NewThread("Starter"));
        var thread = await dbContext.ModernForumThreads.SingleAsync(item => item.LegacyTopicId == created.TopicId);
        await database.SeedPostAsync(
            900, 9000, created.TopicId, thread.Id, 10, "Hidden", "hidden body", isHidden: true);
        await database.ExecuteAsync($"DELETE FROM dbo.ModernForumThreadReadStats WHERE ThreadId = {thread.Id};");

        await repository.CreatePostAsync(new NewForumPost(
            created.TopicId, AuthorId, "Roger", "<p>Visible reply</p>", CreatedAt.AddMinutes(1)));

        Assert.Equal(2, await ScalarIntAsync($"SELECT PostCount FROM dbo.ModernForumThreadReadStats WHERE ThreadId = {thread.Id}"));
    }

    [Fact]
    public async Task Stats_failure_rolls_back_forum_write()
    {
        await SeedWritableCategoryAsync();
        await database.SeedCategoryStatsAsync(1, 10, 0, 0);
        await database.SeedArchiveStatsAsync(0, 0);
        await database.ExecuteAsync("""
            ALTER TABLE dbo.ModernForumArchiveReadStats
            ADD CONSTRAINT CK_ForumWriteTest_FailStats CHECK (TotalThreads = 0);
            """);
        await database.InstallSequencesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => repository.CreateThreadAsync(NewThread("Will roll back")));

        await using var fresh = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(
                database.ConnectionString,
                sql => sql.EnableRetryOnFailure(
                    QueenZoneSqlServerOptions.MaxRetryCount,
                    QueenZoneSqlServerOptions.MaxRetryDelay,
                    errorNumbersToAdd: null))
            .Options);
        Assert.Empty(fresh.ModernForumThreads);
        Assert.Empty(fresh.ModernForumPosts);
        Assert.Equal(0, await fresh.ModernForumCategories.Select(category => category.LegacyPostCount).SingleAsync());
    }

    private Task SeedWritableCategoryAsync() =>
        database.SeedCategoryAsync(1, 10, "General", sortOrder: 1);

    private NewForumThread NewThread(string subject) =>
        new(10, AuthorId, "Freddie", subject, "<p>Hello</p>", CreatedAt);

    private async Task<(int TotalThreads, int Validated)> CategoryStatsAsync(int categoryId)
    {
        var total = await ScalarIntAsync(
            $"SELECT TotalThreads FROM dbo.ModernForumCategoryReadStats WHERE CategoryId = {categoryId}");
        var validated = await ScalarIntAsync(
            $"SELECT ValidatedDisplayThreads FROM dbo.ModernForumCategoryReadStats WHERE CategoryId = {categoryId}");
        return (total, validated);
    }

    private async Task<(int TotalThreads, int Sitemap)> ArchiveStatsAsync()
    {
        var total = await ScalarIntAsync("SELECT TotalThreads FROM dbo.ModernForumArchiveReadStats WHERE Id = 1");
        var sitemap = await ScalarIntAsync("SELECT SitemapTopicCount FROM dbo.ModernForumArchiveReadStats WHERE Id = 1");
        return (total, sitemap);
    }

    private async Task<int> ScalarIntAsync(string sql)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<DateTime> ScalarDateAsync(string sql)
    {
        await using var command = dbContext.Database.GetDbConnection().CreateCommand();
        if (command.Connection!.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync();
        }

        command.CommandText = sql;
        return (DateTime)(await command.ExecuteScalarAsync())!;
    }
}
