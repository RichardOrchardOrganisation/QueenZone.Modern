using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using QueenZone.Data;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

public sealed class PublicReadPerformanceMigrationTests : IAsyncLifetime
{
    private readonly ModernForumSqlServerDatabase database = new();
    private QueenZoneDbContext context = null!;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        context = database.CreateContext();
        await context.Database.ExecuteSqlRawAsync(SearchDocumentSchema.CreateTableSql);
        await ApplyAsync(up: true);
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task Migration_UpDownUp_PreservesDataAndRestoresIndexes()
    {
        await database.SeedCategoryAsync(1, 10, "General", 1);
        await database.SeedThreadAsync(10, 100, 10, 1, "Queen", "Freddie");
        await database.SeedPostAsync(20, 200, 100, 10, 10, "Freddie", "Body");
        Assert.Equal(3, await IndexCountAsync());
        await ApplyAsync(up: false);
        Assert.Equal(0, await IndexCountAsync());
        await ApplyAsync(up: true);
        Assert.Equal(3, await IndexCountAsync());
        var page = await new ModernForumRepository(context).GetTopicPostsPageAsync(100, 1, 20);
        Assert.Equal("Body", Assert.Single(page!.Posts).Body);
    }

    [Fact]
    public async Task Discussion_WithHiddenStarterAndDateTies_PreservesVisibleReplyOrder()
    {
        var date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await database.SeedCategoryAsync(1, 10, "General", 1);
        await database.SeedThreadAsync(10, 100, 10, 1, "Queen", "Freddie", replyCount: 2);
        await database.SeedPostAsync(20, 200, 100, 10, 10, "Hidden", "Hidden", postedAt: date, isHidden: true);
        await database.SeedPostAsync(21, 201, 100, 10, 10, "Starter", "Starter", postedAt: date);
        await database.SeedPostAsync(22, 202, 100, 10, 10, "Brian", "First reply", postedAt: date);
        await database.SeedPostAsync(23, 203, 100, 10, 10, "Roger", "Second reply", postedAt: date);
        var result = await new EfNewsForumDiscussionLookup(context).GetDiscussionAsync(100, 2);
        Assert.Equal(2, result.ReplyCount);
        Assert.Equal(new[] { "Brian", "Roger" }, result.Preview.Select(reply => reply.AuthorDisplayName));
        var page = await new ModernForumRepository(context).GetTopicPostsPageAsync(100, 2, 2);
        Assert.Equal(203, Assert.Single(page!.Posts).Id);
        Assert.Empty((await new ModernForumRepository(context).GetTopicPostsPageAsync(100, 3, 2))!.Posts);
    }

    private async Task<int> IndexCountAsync() =>
        await context.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*) AS Value FROM sys.indexes
            WHERE name IN ('IX_SearchDocument_CandidateMetadata',
                'IX_ModernForumPost_VisibleDiscussion', 'IX_ModernForumThread_PublicRecent')
            """).SingleAsync();

    private async Task ApplyAsync(bool up)
    {
        var migration = new OptimizePublicReadQueries();
        var commands = context.GetService<IMigrationsSqlGenerator>()
            .Generate(up ? migration.UpOperations : migration.DownOperations);
        foreach (var command in commands)
        {
            await context.Database.ExecuteSqlRawAsync(command.CommandText);
        }
    }
}
