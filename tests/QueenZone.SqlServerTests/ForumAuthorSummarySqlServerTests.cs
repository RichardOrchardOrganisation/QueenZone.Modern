using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

public sealed class ForumAuthorSummarySqlServerTests : IAsyncLifetime
{
    private readonly ModernForumSqlServerDatabase database = new();
    private QueenZoneDbContext context = null!;

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        context = database.CreateContext();
        await database.ExecuteAsync("""
            ALTER TABLE dbo.ModernForumPost ALTER COLUMN AuthorDisplayName
                nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL;
            ALTER TABLE dbo.ModernForumThread ALTER COLUMN StartedByDisplayName
                nvarchar(100) COLLATE SQL_Latin1_General_CP1_CI_AS NOT NULL;
            """);
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Summary_preserves_author_identity_counts_and_production_collation(bool hidden)
    {
        var memberId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        await database.SeedCategoryAsync(1, 1, "General", 1);
        await SeedThreadAsync(10, "Forum Fan", memberId, hidden);
        await SeedThreadAsync(11, "Former Name", memberId, hidden);
        await SeedThreadAsync(12, "forum fan", null, hidden);
        await SeedThreadAsync(13, "Forum Fan", otherId, false);
        await SeedThreadAsync(14, "Forum Fán", null, false);
        await database.SeedPostAsync(100, 100, 10, 10, 1, "Forum Fan", "Linked reply",
            authorMemberId: memberId, isHidden: hidden);
        await database.SeedPostAsync(101, 101, 10, 10, 1, "FORUM FAN", "Unlinked reply",
            isHidden: hidden);

        var repository = new EfForumWriteRepository(context);
        var summary = await repository.GetAuthorForumContentSummaryAsync(memberId, "  Forum Fan  ");
        Assert.Equal(5, summary.PostCount);
        Assert.Equal(3, summary.ThreadCount);
        Assert.Equal(hidden, summary.IsHidden);

        var unlinked = await repository.GetAuthorForumContentSummaryAsync(null, "Forum Fan");
        Assert.Equal(2, unlinked.PostCount);
        Assert.Equal(1, unlinked.ThreadCount);
        Assert.Equal(hidden, unlinked.IsHidden);

        var missing = await repository.GetAuthorForumContentSummaryAsync(Guid.NewGuid(), "Missing");
        Assert.Equal(0, missing.PostCount);
        Assert.Equal(0, missing.ThreadCount);
        Assert.False(missing.IsHidden);
    }

    private async Task SeedThreadAsync(long id, string name, Guid? memberId, bool hidden)
    {
        await database.SeedThreadAsync(id, (int)id, 1, 1, $"Thread {id}", name, isHidden: hidden);
        await database.SeedPostAsync(id, (int)id, (int)id, id, 1, name, "Starter",
            authorMemberId: memberId, isHidden: hidden);
    }
}
