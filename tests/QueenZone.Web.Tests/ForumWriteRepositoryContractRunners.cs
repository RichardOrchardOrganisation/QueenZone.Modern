using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class InMemoryForumWriteRepositoryContractTests : ForumWriteRepositoryContractTests
{
    protected override IForumWriteRepository Repository { get; } = new InMemoryForumWriteRepository();

    protected override Task<(Guid Id, string DisplayName)> SeedMemberAsync(string displayName) =>
        Task.FromResult((Guid.NewGuid(), displayName));

    protected override Task<int> SeedCategoryAsync() =>
        Repository.EnsureCategoryAsync($"contract-{Guid.NewGuid():N}", $"Contract {Guid.NewGuid():N}");
}

public sealed class EfForumWriteRepositoryContractTests : ForumWriteRepositoryContractTests, IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;

    public EfForumWriteRepositoryContractTests()
    {
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection).Options);
        dbContext.Database.EnsureCreated();
        CreateModernForumTables();
        Repository = new EfForumWriteRepository(dbContext);
    }

    protected override IForumWriteRepository Repository { get; }

    protected override async Task<(Guid Id, string DisplayName)> SeedMemberAsync(string displayName)
    {
        var email = $"{Guid.NewGuid():N}@example.com";
        var member = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = displayName,
            CreatedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        dbContext.MemberAccounts.Add(member);
        await dbContext.SaveChangesAsync();
        return (member.Id, member.DisplayName);
    }

    protected override Task<int> SeedCategoryAsync() =>
        Repository.EnsureCategoryAsync($"contract-{Guid.NewGuid():N}", $"Contract {Guid.NewGuid():N}");

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
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
