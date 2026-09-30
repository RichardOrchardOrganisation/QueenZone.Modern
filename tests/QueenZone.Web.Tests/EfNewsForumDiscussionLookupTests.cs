using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfNewsForumDiscussionLookupTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;
    private readonly EfNewsForumDiscussionLookup lookup;

    public EfNewsForumDiscussionLookupTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        CreateModernForumTables();
        lookup = new EfNewsForumDiscussionLookup(dbContext);
    }

    [Fact]
    public async Task GetReplyCountsAsync_BatchesDistinctTopicIds_WithoutBodies()
    {
        await SeedThreadAsync(11, replyCount: 4);
        await SeedThreadAsync(12, replyCount: 0);

        var counts = await lookup.GetReplyCountsAsync([11, 12, 11, 99]);

        Assert.Equal(4, counts[11]);
        Assert.Equal(0, counts[12]);
        Assert.False(counts.ContainsKey(99));
    }

    [Fact]
    public async Task GetDiscussionAsync_ReturnsLastTwoReplies_NotOpeningPost()
    {
        var topicId = 77;
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var category = new ModernForumCategoryEntity
        {
            LegacyForumId = 9,
            Name = NewsForumDiscussion.CategoryName,
            SortOrder = 90,
            LegacyPostCount = 4,
            ImportedAt = now,
            UpdatedAt = now,
        };
        dbContext.ModernForumCategories.Add(category);
        await dbContext.SaveChangesAsync();

        var thread = new ModernForumThreadEntity
        {
            LegacyTopicId = topicId,
            LegacyForumId = 9,
            CategoryId = category.Id,
            Title = "News discussion",
            StartedByDisplayName = NewsForumDiscussion.SystemMemberDisplayName,
            StartedAt = now,
            LastActivityAt = now.AddHours(3),
            ReplyCount = 3,
            ImportedAt = now,
            UpdatedAt = now,
        };
        dbContext.ModernForumThreads.Add(thread);
        await dbContext.SaveChangesAsync();

        dbContext.ModernForumPosts.AddRange(
            Post(thread, 1, NewsForumDiscussion.SystemMemberDisplayName, now, "Opening excerpt plus link"),
            Post(thread, 2, "Alice", now.AddHours(1), "first reply"),
            Post(thread, 3, "Bob", now.AddHours(2), "second reply"),
            Post(thread, 4, "Cara", now.AddHours(3), "third reply"));
        await dbContext.SaveChangesAsync();

        var discussion = await lookup.GetDiscussionAsync(topicId, NewsForumDiscussion.PreviewReplyCount);

        Assert.Equal(3, discussion.ReplyCount);
        Assert.Equal(2, discussion.Preview.Count);
        Assert.Equal("Bob", discussion.Preview[0].AuthorDisplayName);
        Assert.Equal("Cara", discussion.Preview[1].AuthorDisplayName);
        Assert.DoesNotContain(
            discussion.Preview,
            preview => preview.Excerpt.Contains("Opening", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetDiscussionAsync_ReturnsOneReply_WhenOnlyOneReplyExists()
    {
        var topicId = 78;
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var category = new ModernForumCategoryEntity
        {
            LegacyForumId = 10,
            Name = NewsForumDiscussion.CategoryName,
            SortOrder = 100,
            LegacyPostCount = 2,
            ImportedAt = now,
            UpdatedAt = now,
        };
        dbContext.ModernForumCategories.Add(category);
        await dbContext.SaveChangesAsync();

        var thread = new ModernForumThreadEntity
        {
            LegacyTopicId = topicId,
            LegacyForumId = 10,
            CategoryId = category.Id,
            Title = "Single reply discussion",
            StartedByDisplayName = NewsForumDiscussion.SystemMemberDisplayName,
            StartedAt = now,
            LastActivityAt = now.AddHours(1),
            ReplyCount = 1,
            ImportedAt = now,
            UpdatedAt = now,
        };
        dbContext.ModernForumThreads.Add(thread);
        await dbContext.SaveChangesAsync();

        dbContext.ModernForumPosts.AddRange(
            Post(thread, 11, NewsForumDiscussion.SystemMemberDisplayName, now, "Opening excerpt plus link"),
            Post(thread, 12, "Only", now.AddHours(1), "sole reply"));
        await dbContext.SaveChangesAsync();

        var discussion = await lookup.GetDiscussionAsync(topicId, NewsForumDiscussion.PreviewReplyCount);

        Assert.Equal(1, discussion.ReplyCount);
        var preview = Assert.Single(discussion.Preview);
        Assert.Equal("Only", preview.AuthorDisplayName);
        Assert.Equal("sole reply", preview.Excerpt);
    }

    [Fact]
    public async Task GetDiscussionAsync_WhenNoThreadExists_ReturnsMissingWithoutUsingPosts()
    {
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var category = new ModernForumCategoryEntity
        {
            LegacyForumId = 11,
            Name = NewsForumDiscussion.CategoryName,
            SortOrder = 110,
            LegacyPostCount = 2,
            ImportedAt = now,
            UpdatedAt = now,
        };
        dbContext.ModernForumCategories.Add(category);
        await dbContext.SaveChangesAsync();

        var otherThread = new ModernForumThreadEntity
        {
            LegacyTopicId = 79,
            LegacyForumId = 11,
            CategoryId = category.Id,
            Title = "Unrelated topic",
            StartedByDisplayName = NewsForumDiscussion.SystemMemberDisplayName,
            StartedAt = now,
            LastActivityAt = now,
            ReplyCount = 1,
            ImportedAt = now,
            UpdatedAt = now,
        };
        dbContext.ModernForumThreads.Add(otherThread);
        await dbContext.SaveChangesAsync();
        var orphanReply = Post(otherThread, 22, "Orphan", now.AddHours(1), "must not be used for a missing topic");
        orphanReply.LegacyThreadTopicId = 1175833020;
        dbContext.ModernForumPosts.AddRange(
            Post(otherThread, 21, NewsForumDiscussion.SystemMemberDisplayName, now, "Opening"),
            orphanReply);
        await dbContext.SaveChangesAsync();

        var discussion = await lookup.GetDiscussionAsync(1175833020, NewsForumDiscussion.PreviewReplyCount);

        Assert.False(discussion.ThreadFound);
        Assert.Null(discussion.ReplyCount);
        Assert.Empty(discussion.Preview);
    }

    [Fact]
    public async Task GetDiscussionAsync_WhenThreadIsHidden_ReturnsMissingWithoutUsingPosts()
    {
        var topicId = 80;
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var category = new ModernForumCategoryEntity
        {
            LegacyForumId = 12,
            Name = NewsForumDiscussion.CategoryName,
            SortOrder = 120,
            LegacyPostCount = 2,
            ImportedAt = now,
            UpdatedAt = now,
        };
        dbContext.ModernForumCategories.Add(category);
        await dbContext.SaveChangesAsync();

        var thread = new ModernForumThreadEntity
        {
            LegacyTopicId = topicId,
            LegacyForumId = 12,
            CategoryId = category.Id,
            Title = "Hidden discussion",
            StartedByDisplayName = NewsForumDiscussion.SystemMemberDisplayName,
            StartedAt = now,
            LastActivityAt = now.AddHours(1),
            ReplyCount = 1,
            IsHidden = true,
            ImportedAt = now,
            UpdatedAt = now,
        };
        dbContext.ModernForumThreads.Add(thread);
        await dbContext.SaveChangesAsync();
        dbContext.ModernForumPosts.AddRange(
            Post(thread, 31, NewsForumDiscussion.SystemMemberDisplayName, now, "Opening excerpt plus link"),
            Post(thread, 32, "HiddenReply", now.AddHours(1), "must not become a preview"));
        await dbContext.SaveChangesAsync();

        var discussion = await lookup.GetDiscussionAsync(topicId, NewsForumDiscussion.PreviewReplyCount);

        Assert.False(discussion.ThreadFound);
        Assert.Null(discussion.ReplyCount);
        Assert.Empty(discussion.Preview);
    }

    [Fact]
    public async Task GetDiscussionAsync_WhenVisibleThreadHasZeroReplies_ReturnsZeroReplyCount()
    {
        var topicId = 81;
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var category = new ModernForumCategoryEntity
        {
            LegacyForumId = 13,
            Name = NewsForumDiscussion.CategoryName,
            SortOrder = 130,
            LegacyPostCount = 1,
            ImportedAt = now,
            UpdatedAt = now,
        };
        dbContext.ModernForumCategories.Add(category);
        await dbContext.SaveChangesAsync();

        var thread = new ModernForumThreadEntity
        {
            LegacyTopicId = topicId,
            LegacyForumId = 13,
            CategoryId = category.Id,
            Title = "Opening post only",
            StartedByDisplayName = NewsForumDiscussion.SystemMemberDisplayName,
            StartedAt = now,
            LastActivityAt = now,
            ReplyCount = 0,
            ImportedAt = now,
            UpdatedAt = now,
        };
        dbContext.ModernForumThreads.Add(thread);
        await dbContext.SaveChangesAsync();
        dbContext.ModernForumPosts.Add(
            Post(thread, 41, NewsForumDiscussion.SystemMemberDisplayName, now, "Opening excerpt plus link"));
        await dbContext.SaveChangesAsync();

        var discussion = await lookup.GetDiscussionAsync(topicId, NewsForumDiscussion.PreviewReplyCount);

        Assert.True(discussion.ThreadFound);
        Assert.Equal(0, discussion.ReplyCount);
        Assert.Empty(discussion.Preview);
    }

    [Fact]
    public async Task GetReplyCountsAsync_EmptyInput_ReturnsEmpty()
    {
        var counts = await lookup.GetReplyCountsAsync([]);
        Assert.Empty(counts);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task SeedThreadAsync(int topicId, int replyCount)
    {
        var now = DateTime.UtcNow;
        var category = await dbContext.ModernForumCategories.FirstOrDefaultAsync();
        if (category is null)
        {
            category = new ModernForumCategoryEntity
            {
                LegacyForumId = 9,
                Name = NewsForumDiscussion.CategoryName,
                SortOrder = 90,
                LegacyPostCount = 0,
                ImportedAt = now,
                UpdatedAt = now,
            };
            dbContext.ModernForumCategories.Add(category);
            await dbContext.SaveChangesAsync();
        }

        dbContext.ModernForumThreads.Add(new ModernForumThreadEntity
        {
            LegacyTopicId = topicId,
            LegacyForumId = category.LegacyForumId,
            CategoryId = category.Id,
            Title = $"Topic {topicId}",
            StartedByDisplayName = "QueenZone",
            StartedAt = now,
            LastActivityAt = now,
            ReplyCount = replyCount,
            ImportedAt = now,
            UpdatedAt = now,
        });
        await dbContext.SaveChangesAsync();
    }

    private static ModernForumPostEntity Post(
        ModernForumThreadEntity thread,
        int legacyPostId,
        string author,
        DateTime postedAt,
        string body) =>
        new()
        {
            LegacyPostId = legacyPostId,
            LegacyThreadTopicId = thread.LegacyTopicId,
            ThreadId = thread.Id,
            LegacyForumId = thread.LegacyForumId,
            AuthorDisplayName = author,
            BodyHtml = $"<p>{body}</p>",
            PostedAt = postedAt,
            ImportedAt = postedAt,
            UpdatedAt = postedAt,
        };

    private void CreateModernForumTables()
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE ModernForumCategory
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyForumId INTEGER NOT NULL UNIQUE,
                Name TEXT NOT NULL,
                Description TEXT NULL,
                SortOrder INTEGER NOT NULL,
                LegacyPostCount INTEGER NOT NULL,
                LastActivityAt TEXT NULL,
                IsSynthetic INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE ModernForumThread
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyTopicId INTEGER NOT NULL UNIQUE,
                LegacyForumId INTEGER NOT NULL,
                CategoryId INTEGER NOT NULL,
                Title TEXT NOT NULL,
                StartedByLegacyUserId INTEGER NULL,
                StartedByDisplayName TEXT NOT NULL,
                StartedAt TEXT NULL,
                LastActivityAt TEXT NULL,
                ReplyCount INTEGER NOT NULL,
                IsSticky INTEGER NOT NULL,
                IsLegacyTopicStarter INTEGER NOT NULL,
                LegacyDiscography INTEGER NOT NULL,
                StartedByUserValidated INTEGER NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                StarterAttachment TEXT NULL,
                StarterFileSize TEXT NULL,
                StarterAttachCount INTEGER NOT NULL,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (CategoryId) REFERENCES ModernForumCategory (Id)
            );

            CREATE TABLE ModernForumPost
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                LegacyPostId INTEGER NOT NULL UNIQUE,
                LegacyThreadTopicId INTEGER NOT NULL,
                ThreadId INTEGER NOT NULL,
                LegacyForumId INTEGER NOT NULL,
                AuthorLegacyUserId INTEGER NULL,
                AuthorDisplayName TEXT NOT NULL,
                AuthorPostCount INTEGER NULL,
                AuthorJoinedAt TEXT NULL,
                BodyHtml TEXT NOT NULL,
                SignatureHtml TEXT NULL,
                PostedAt TEXT NULL,
                LegacyDiscography INTEGER NOT NULL,
                AuthorUserValidated INTEGER NULL,
                Attachment TEXT NULL,
                FileSize TEXT NULL,
                AttachCount INTEGER NOT NULL,
                AuthorMemberId TEXT NULL,
                EditedAt TEXT NULL,
                EditCount INTEGER NOT NULL DEFAULT 0,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ThreadId) REFERENCES ModernForumThread (Id)
            );
            """);
    }
}
