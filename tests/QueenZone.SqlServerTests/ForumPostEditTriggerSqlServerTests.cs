using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

/// <summary>Exercises the real migration trigger in a disposable database, never the mirror.</summary>
public sealed class ForumPostEditTriggerSqlServerTests : IAsyncLifetime
{
    private readonly ModernForumSqlServerDatabase database = new();
    private static readonly DateTimeOffset OriginalUpdatedAt = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await database.InitializeAsync();
        await ApplyAsync(new AddModernForumPostAuthorLegacyUserIndex());
        await ApplyAsync(new OrderModernForumPostArchiveAuthorIndex());
        await ApplyAsync(new AddModernForumArchiveAuthorSummary());
        await ApplyAsync(new SkipArchiveAuthorSummaryForPostEdits());
        await database.SeedCategoryAsync(1, 1, "Archive", 1);
        await database.SeedThreadAsync(10, 10, 1, 1, "Archived thread", "Synthetic author");
    }

    public Task DisposeAsync() => database.DisposeAsync();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Body_edit_avoids_summary_lock_only_with_guard(bool guarded)
    {
        if (!guarded)
        {
            // Restore the real pre-fix trigger as a control, not an approximation.
            await ApplyAsync(new SkipArchiveAuthorSummaryForPostEdits(), down: true);
        }

        await SeedAsync(100, 1, "Original author", "2000-01-01");
        await SeedAsync(101, 2, "Other author", "2000-01-02");
        await database.ExecuteAsync("""
            UPDATE dbo.ModernForumPost SET UpdatedAt = '2000-01-01' WHERE Id = 100;
            UPDATE dbo.ModernForumArchiveAuthorSummary SET RefreshedAt = '1999-01-01';
            """);
        var summariesBefore = await SummariesAsync();

        await using var blocker = new SqlConnection(database.ConnectionString);
        await blocker.OpenAsync();
        await using var transaction = (SqlTransaction)await blocker.BeginTransactionAsync();
        await using (var command = blocker.CreateCommand())
        {
            command.Transaction = transaction;
            // Lock only the summary row, not the post. Always rolled back below.
            command.CommandText = "UPDATE dbo.ModernForumArchiveAuthorSummary SET RefreshedAt = RefreshedAt WHERE LegacyUserId = 1";
            await command.ExecuteNonQueryAsync();
        }

        await using var context = database.CreateContext();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET LOCK_TIMEOUT 1000;");
        var repository = new EfForumWriteRepository(context);
        try
        {
            if (guarded)
            {
                var result = await repository.UpdatePostAsync(100, Guid.Empty, "<p>Edited</p>",
                    true, 0, OriginalUpdatedAt);
                Assert.Equal(ForumPostUpdateStatus.Success, result.Status);
            }
            else
            {
                // The non-retrying SQL Server execution strategy wraps transient lock timeouts.
                var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    repository.UpdatePostAsync(100, Guid.Empty, "<p>Edited</p>", true, 0, OriginalUpdatedAt));
                var updateException = Assert.IsType<DbUpdateException>(exception.InnerException);
                Assert.Equal(1222, Assert.IsType<SqlException>(updateException.InnerException).Number);
            }
        }
        finally
        {
            await transaction.RollbackAsync();
        }

        Assert.Equal(summariesBefore, await SummariesAsync());
        await using var verify = database.CreateContext();
        var post = await verify.ModernForumPosts.SingleAsync(item => item.LegacyPostId == 100);
        Assert.Equal(guarded ? "<p>Edited</p>" : "<p>Original</p>", post.BodyHtml);
        Assert.Equal(guarded ? 1 : 0, post.EditCount);
        Assert.Equal(1, post.AuthorLegacyUserId);
        Assert.Null(post.AuthorMemberId);
        Assert.Equal("Original author", post.AuthorDisplayName);
        Assert.False(post.IsHidden);
        if (guarded)
        {
            Assert.NotNull(post.EditedAt);
            Assert.True(post.UpdatedAt > OriginalUpdatedAt.UtcDateTime);
        }

        var other = await verify.ModernForumPosts.SingleAsync(item => item.LegacyPostId == 101);
        Assert.Equal("<p>Original</p>", other.BodyHtml);
        Assert.Equal(0, other.EditCount);
        Assert.Null(other.EditedAt);
    }

    [Fact]
    public async Task Multirow_visibility_transfers_and_deletes_keep_counts_and_identity_correct()
    {
        await SeedAsync(100, 1, "First", "2000-01-01");
        await SeedAsync(101, 1, "Latest", "2000-01-02");
        await SeedAsync(102, 2, "Second author", "2000-01-03");
        await SeedAsync(103, null, "Unattributed", "2000-01-04");
        await AssertSummariesAsync((1, "Latest", 2), (2, "Second author", 1));

        // Mixed body + visibility changes must not take the body-only shortcut.
        await database.ExecuteAsync("UPDATE dbo.ModernForumPost SET BodyHtml = N'Hidden', IsHidden = 1 WHERE Id IN (101, 102);");
        await AssertSummariesAsync((1, "First", 1));
        await database.ExecuteAsync("UPDATE dbo.ModernForumPost SET IsHidden = 0 WHERE Id IN (101, 102);");
        await AssertSummariesAsync((1, "Latest", 2), (2, "Second author", 1));

        // One statement affects both the old and new authors, including NULL.
        await database.ExecuteAsync("UPDATE dbo.ModernForumPost SET AuthorLegacyUserId = 2 WHERE Id IN (101, 103);");
        await AssertSummariesAsync((1, "First", 1), (2, "Unattributed", 3));
        await database.ExecuteAsync("UPDATE dbo.ModernForumPost SET AuthorLegacyUserId = NULL WHERE Id IN (100, 103);");
        await AssertSummariesAsync((2, "Second author", 2));
        await database.ExecuteAsync("DELETE FROM dbo.ModernForumPost WHERE Id IN (101, 102);");
        await AssertSummariesAsync();
    }

    [Fact]
    public async Task Identity_and_latest_order_changes_refresh_summary()
    {
        await SeedAsync(100, 1, "Older", "2000-01-01");
        await SeedAsync(101, 1, "Latest", "2000-01-02");
        await database.ExecuteAsync("UPDATE dbo.ModernForumPost SET AuthorDisplayName = N'Renamed' WHERE Id = 101;");
        await AssertSummariesAsync((1, "Renamed", 2));
        await database.ExecuteAsync("UPDATE dbo.ModernForumPost SET AuthorJoinedAt = '1995-01-01' WHERE Id = 101;");
        Assert.Equal(new DateTime(1995, 1, 1), Assert.Single(await SummariesAsync()).MemberSince);
        await database.ExecuteAsync("UPDATE dbo.ModernForumPost SET PostedAt = '2000-01-03' WHERE Id = 100;");
        await AssertSummariesAsync((1, "Older", 2));
        // On a timestamp tie the larger Id remains the source of author identity.
        await database.ExecuteAsync("UPDATE dbo.ModernForumPost SET PostedAt = '2000-01-03' WHERE Id = 101;");
        await AssertSummariesAsync((1, "Renamed", 2));
    }

    [Fact]
    public async Task Multirow_insert_and_delete_maintain_summary_without_an_update()
    {
        await database.ExecuteAsync("""
            INSERT dbo.ModernForumPost
                (LegacyPostId, LegacyThreadTopicId, ThreadId, LegacyForumId,
                 AuthorLegacyUserId, AuthorDisplayName, BodyHtml, PostedAt, LegacyDiscography, AttachCount)
            VALUES (100, 10, 10, 1, 1, N'First', N'Body', '2000-01-01', 0, 0),
                   (101, 10, 10, 1, 1, N'Latest', N'Body', '2000-01-02', 0, 0),
                   (102, 10, 10, 1, 2, N'Other', N'Body', '2000-01-03', 0, 0);
            """);
        await AssertSummariesAsync((1, "Latest", 2), (2, "Other", 1));
        await database.ExecuteAsync("DELETE FROM dbo.ModernForumPost;");
        await AssertSummariesAsync();
    }

    [Fact]
    public async Task Stale_editor_timestamp_still_rejects_the_second_edit()
    {
        await SeedAsync(100, 1, "Original author", "2000-01-01");
        await database.ExecuteAsync("UPDATE dbo.ModernForumPost SET UpdatedAt = '2000-01-01' WHERE Id = 100;");
        await using (var first = database.CreateContext())
        {
            var result = await new EfForumWriteRepository(first).UpdatePostAsync(
                100, Guid.Empty, "First edit", true, 0, OriginalUpdatedAt);
            Assert.Equal(ForumPostUpdateStatus.Success, result.Status);
        }

        await using var second = database.CreateContext();
        var stale = await new EfForumWriteRepository(second).UpdatePostAsync(
            100, Guid.Empty, "Stale edit", true, 0, OriginalUpdatedAt);
        Assert.Equal(ForumPostUpdateStatus.ConcurrencyConflict, stale.Status);
        var post = await second.ModernForumPosts.SingleAsync(item => item.LegacyPostId == 100);
        Assert.Equal("First edit", post.BodyHtml);
        Assert.Equal(1, post.EditCount);
    }

    private Task SeedAsync(long id, int? author, string name, string postedAt) =>
        database.SeedPostAsync(id, (int)id, 10, 10, 1, name, "<p>Original</p>",
            postedAt: DateTime.ParseExact(postedAt, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            authorLegacyUserId: author);

    private async Task ApplyAsync(Migration migration, bool down = false)
    {
        foreach (var operation in (down ? migration.DownOperations : migration.UpOperations).OfType<SqlOperation>())
        {
            await database.ExecuteAsync(operation.Sql);
        }
    }

    private async Task<Summary[]> SummariesAsync()
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT LegacyUserId, DisplayName, MemberSince, PostCount, RefreshedAt FROM dbo.ModernForumArchiveAuthorSummary ORDER BY LegacyUserId";
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<Summary>();
        while (await reader.ReadAsync())
        {
            result.Add(new Summary(reader.GetInt32(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2), reader.GetInt32(3), reader.GetDateTime(4)));
        }

        return result.ToArray();
    }

    private async Task AssertSummariesAsync(params (int Id, string Name, int Count)[] expected) =>
        Assert.Equal(expected, (await SummariesAsync()).Select(row => (row.Id, row.Name, row.Count)).ToArray());

    private sealed record Summary(int Id, string Name, DateTime? MemberSince, int Count, DateTime RefreshedAt);
}
