using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class ModernForumRepositoryRecentThreadsTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;
    private readonly ModernForumRepository repository;

    public ModernForumRepositoryRecentThreadsTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        CreateModernForumTables();
        repository = new ModernForumRepository(dbContext);
    }

    [Fact]
    public async Task GetRecentThreadsAsync_OrdersByLastActivityAndSkipsInvalidRows()
    {
        var music = await SeedCategoryAsync(legacyForumId: 1, name: "The Music", isSynthetic: false);
        var lounge = await SeedCategoryAsync(legacyForumId: 2, name: "The Lounge", isSynthetic: false);
        await SeedCategoryAsync(legacyForumId: 99, name: "Synthetic", isSynthetic: true);

        await SeedThreadAsync(music.Id, 1, legacyTopicId: 10, title: "Older music", replyCount: 2, lastActivityAt: new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc), validated: true);
        await SeedThreadAsync(lounge.Id, 2, legacyTopicId: 20, title: "Newest lounge", replyCount: 7, lastActivityAt: new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc), validated: true);
        await SeedThreadAsync(music.Id, 1, legacyTopicId: 11, title: "Unvalidated", replyCount: 9, lastActivityAt: new DateTime(2026, 8, 4, 12, 0, 0, DateTimeKind.Utc), validated: false);
        await SeedThreadAsync(music.Id, 1, legacyTopicId: 12, title: "Not a starter", replyCount: 1, lastActivityAt: new DateTime(2026, 8, 4, 13, 0, 0, DateTimeKind.Utc), validated: true, isStarter: false);
        await SeedThreadAsync(music.Id, 1, legacyTopicId: 13, title: "Suspended starter", replyCount: 3, lastActivityAt: new DateTime(2026, 8, 5, 13, 0, 0, DateTimeKind.Utc), validated: true, isHidden: true);

        var recent = await repository.GetRecentThreadsAsync(10);

        Assert.Equal(2, recent.Count);
        Assert.Equal(20, recent[0].TopicId);
        Assert.Equal("Newest lounge", recent[0].Title);
        Assert.Equal("The Lounge", recent[0].CategoryName);
        Assert.Equal(7, recent[0].ReplyCount);
        Assert.Equal(10, recent[1].TopicId);
        Assert.Equal("The Music", recent[1].CategoryName);
    }

    [Fact]
    public async Task GetRecentThreadsAsync_RespectsCountClamp()
    {
        var music = await SeedCategoryAsync(legacyForumId: 1, name: "The Music", isSynthetic: false);
        for (var i = 1; i <= 5; i++)
        {
            await SeedThreadAsync(
                music.Id,
                1,
                legacyTopicId: 100 + i,
                title: $"Thread {i}",
                replyCount: i,
                lastActivityAt: new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i),
                validated: true);
        }

        var recent = await repository.GetRecentThreadsAsync(3);

        Assert.Equal(3, recent.Count);
        Assert.Equal([105, 104, 103], recent.Select(item => item.TopicId).ToArray());
    }

    [Fact]
    public async Task GetRecentThreadsAsync_ExcludesWebsiteBoardBeforeLimitAndKeepsOtherTestTitles()
    {
        var website = await SeedCategoryAsync(7, "Queenzone.com", false);
        var music = await SeedCategoryAsync(1, "The Music", false);
        var now = new DateTime(2026, 8, 3, 0, 0, 0, DateTimeKind.Utc);
        await SeedThreadAsync(website.Id, 7, 200, "Reviewer Test Message", 0, now.AddDays(1), true);
        await SeedThreadAsync(music.Id, 1, 201, "Test pressings of Queen II", 3, now, true);
        await SeedThreadAsync(music.Id, 1, 202, "Older discussion", 2, now.AddDays(-1), true);

        var recent = await repository.GetRecentThreadsAsync(2);

        Assert.Equal([201, 202], recent.Select(item => item.TopicId).ToArray());
        Assert.All(recent, item => Assert.NotEqual(7, item.CategoryId));
        // Promotion is separate from visibility: neither the board nor its rows are hidden.
        Assert.False(website.IsSynthetic);
        Assert.False((await dbContext.ModernForumThreads.SingleAsync(thread => thread.LegacyTopicId == 200)).IsHidden);
    }

    private async Task<ModernForumCategoryEntity> SeedCategoryAsync(int legacyForumId, string name, bool isSynthetic)
    {
        var category = new ModernForumCategoryEntity
        {
            LegacyForumId = legacyForumId,
            Name = name,
            Description = name,
            SortOrder = legacyForumId,
            LegacyPostCount = 0,
            IsSynthetic = isSynthetic,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        dbContext.ModernForumCategories.Add(category);
        await dbContext.SaveChangesAsync();
        return category;
    }

    private async Task SeedThreadAsync(
        int categoryId,
        int legacyForumId,
        int legacyTopicId,
        string title,
        int replyCount,
        DateTime lastActivityAt,
        bool validated,
        bool isStarter = true,
        bool isHidden = false)
    {
        dbContext.ModernForumThreads.Add(new ModernForumThreadEntity
        {
            LegacyTopicId = legacyTopicId,
            LegacyForumId = legacyForumId,
            CategoryId = categoryId,
            Title = title,
            StartedByDisplayName = "member",
            LastActivityAt = lastActivityAt,
            ReplyCount = replyCount,
            IsSticky = false,
            IsLegacyTopicStarter = isStarter,
            LegacyDiscography = 0,
            StartedByUserValidated = validated,
            IsHidden = isHidden,
            StarterAttachCount = 0,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

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
            """);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
