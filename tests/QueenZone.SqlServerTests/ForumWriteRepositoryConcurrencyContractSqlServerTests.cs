using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

public sealed class ForumWriteRepositoryConcurrencyContractSqlServerTests : IAsyncLifetime
{
    private readonly ModernForumSqlServerDatabase database = new();
    private static readonly DateTimeOffset Start = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        await database.InstallSequencesAsync();
        await ApplyAsync(new AddModernForumPostAuthorLegacyUserIndex());
        await ApplyAsync(new OrderModernForumPostArchiveAuthorIndex());
        await ApplyAsync(new AddModernForumArchiveAuthorSummary());
        await ApplyAsync(new SkipArchiveAuthorSummaryForPostEdits());
        await database.SeedCategoryAsync(1, 1, "Contract discussion", 1);
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Fact]
    public async Task Update_SeparateContextsRejectStaleExpectedTimestamp()
    {
        var memberId = Guid.NewGuid();
        var first = new EfForumWriteRepository(database.CreateContext());
        var second = new EfForumWriteRepository(database.CreateContext());
        var created = await first.CreateThreadAsync(new NewForumThread(
            1, memberId, "Alice", "Discussion", "Original", Start));
        var original = await second.GetPostAsync(created.StarterPostId);
        Assert.NotNull(original);
        Assert.NotNull(original.UpdatedAt);

        var success = await first.UpdatePostAsync(created.StarterPostId, memberId, "First edit", false, -1, original.UpdatedAt);
        Assert.Equal(ForumPostUpdateStatus.Success, success.Status);
        var conflict = await second.UpdatePostAsync(created.StarterPostId, memberId, "Stale edit", false, -1, original.UpdatedAt);
        Assert.Equal(ForumPostUpdateStatus.ConcurrencyConflict, conflict.Status);
        var stored = await second.GetPostAsync(created.StarterPostId);
        Assert.NotNull(stored);
        Assert.Equal("First edit", stored.Body);
        Assert.Equal(1, stored.EditCount);
    }

    [Fact]
    public async Task Update_StaleTrackedPostCannotOverwriteFirstContextsSave()
    {
        var memberId = Guid.NewGuid();
        var first = new EfForumWriteRepository(database.CreateContext());
        var secondContext = database.CreateContext();
        var second = new EfForumWriteRepository(secondContext);
        var created = await first.CreateThreadAsync(new NewForumThread(
            1, memberId, "Alice", "Discussion", "Original", Start));
        var original = await second.GetPostAsync(created.StarterPostId);
        Assert.NotNull(original);
        Assert.NotNull(original.UpdatedAt);
        await secondContext.ModernForumPosts.Include(post => post.Thread)
            .Where(post => post.LegacyPostId == created.StarterPostId).LoadAsync();

        var success = await first.UpdatePostAsync(created.StarterPostId, memberId, "First edit", false, -1, original.UpdatedAt);
        Assert.Equal(ForumPostUpdateStatus.Success, success.Status);
        var conflict = await second.UpdatePostAsync(created.StarterPostId, memberId, "Stale edit", false, -1, original.UpdatedAt);
        Assert.Equal(ForumPostUpdateStatus.ConcurrencyConflict, conflict.Status);
        var stored = await new EfForumWriteRepository(database.CreateContext()).GetPostAsync(created.StarterPostId);
        Assert.NotNull(stored);
        Assert.Equal("First edit", stored.Body);
        Assert.Equal(1, stored.EditCount);
        Assert.NotEqual(original.UpdatedAt, stored.UpdatedAt);
    }

    private async Task ApplyAsync(Migration migration)
    {
        foreach (var operation in migration.UpOperations.OfType<SqlOperation>())
        {
            await database.ExecuteAsync(operation.Sql);
        }
    }
}
