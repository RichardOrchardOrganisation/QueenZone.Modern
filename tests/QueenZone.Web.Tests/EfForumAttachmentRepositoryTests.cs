using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfForumAttachmentRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;
    private readonly EfForumWriteRepository writeRepository;
    private readonly EfForumAttachmentRepository attachmentRepository;

    public EfForumAttachmentRepositoryTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        CreateModernForumTables();
        writeRepository = new EfForumWriteRepository(dbContext);
        attachmentRepository = new EfForumAttachmentRepository(dbContext);
    }

    [Fact]
    public async Task AddAttachmentsAsync_PersistsRowsForPost()
    {
        var member = await SeedMemberAsync();
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1,
            member.Id,
            member.DisplayName,
            "With attachments",
            "<p>Body</p>",
            DateTimeOffset.UtcNow));

        await attachmentRepository.AddAttachmentsAsync(
            created.StarterPostId,
            [
                new NewForumAttachment(
                    "scan.pdf",
                    "members/abc/scan.pdf",
                    "ugc-forum",
                    4096,
                    "application/pdf",
                    DateTimeOffset.Parse("2026-07-11T12:00:00Z")),
            ]);

        var stored = await attachmentRepository.GetByLegacyPostIdsAsync([created.StarterPostId]);
        Assert.Single(stored);
        Assert.Equal("scan.pdf", stored[0].OriginalFileName);
        Assert.Equal("members/abc/scan.pdf", stored[0].BlobPath);
        Assert.Equal(0, stored[0].DownloadCount);

        var post = await dbContext.ModernForumPosts.SingleAsync(item => item.LegacyPostId == created.StarterPostId);
        Assert.Equal(1, post.AttachCount);
    }

    [Fact]
    public async Task IncrementDownloadCountAsync_BumpsCounter()
    {
        var member = await SeedMemberAsync();
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1,
            member.Id,
            member.DisplayName,
            "Downloads",
            "<p>Body</p>",
            DateTimeOffset.UtcNow));

        await attachmentRepository.AddAttachmentsAsync(
            created.StarterPostId,
            [
                new NewForumAttachment(
                    "a.txt",
                    "members/x/a.txt",
                    "ugc-forum",
                    12,
                    "text/plain",
                    DateTimeOffset.UtcNow),
            ]);

        var attachment = (await attachmentRepository.GetByLegacyPostIdsAsync([created.StarterPostId])).Single();
        await attachmentRepository.IncrementDownloadCountAsync(attachment.Id);
        await attachmentRepository.IncrementDownloadCountAsync(attachment.Id);

        var reloaded = await attachmentRepository.GetAsync(created.StarterPostId, attachment.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(2, reloaded!.DownloadCount);
    }

    [Fact]
    public async Task AddAttachmentsAsync_NoOpsWhenEmpty()
    {
        await attachmentRepository.AddAttachmentsAsync(1, []);
        Assert.Empty(await attachmentRepository.GetByLegacyPostIdsAsync([1]));
    }

    [Fact]
    public async Task GetByLegacyPostIdsAsync_ReturnsEmptyForEmptyInput()
    {
        Assert.Empty(await attachmentRepository.GetByLegacyPostIdsAsync([]));
    }

    [Fact]
    public async Task AddAttachmentsAsync_ThrowsWhenPostMissing()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            attachmentRepository.AddAttachmentsAsync(
                404,
                [new NewForumAttachment("a.txt", "p/a.txt", "ugc-forum", 1, "text/plain", DateTimeOffset.UtcNow)]));
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenMissing()
    {
        Assert.Null(await attachmentRepository.GetAsync(1, Guid.NewGuid()));
    }

    [Fact]
    public async Task GetLegacyAsync_ReturnsNullWhenPostMissingOrNoAttachment()
    {
        Assert.Null(await attachmentRepository.GetLegacyAsync(404));

        var member = await SeedMemberAsync();
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1,
            member.Id,
            member.DisplayName,
            "No attach",
            "<p>Body</p>",
            DateTimeOffset.UtcNow));

        Assert.Null(await attachmentRepository.GetLegacyAsync(created.StarterPostId));
    }

    [Fact]
    public async Task IncrementDownloadCountAsync_NoOpsWhenMissing()
    {
        var exception = await Record.ExceptionAsync(() => attachmentRepository.IncrementDownloadCountAsync(Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task AddAttachmentsAsync_TruncatesLongMetadata()
    {
        var member = await SeedMemberAsync();
        await SeedCategoryAsync();
        var created = await writeRepository.CreateThreadAsync(new NewForumThread(
            1,
            member.Id,
            member.DisplayName,
            "Long names",
            "<p>Body</p>",
            DateTimeOffset.UtcNow));

        var longName = new string('n', 300) + ".pdf";
        var longPath = new string('p', 600);
        var longMime = new string('m', 150);

        await attachmentRepository.AddAttachmentsAsync(
            created.StarterPostId,
            [
                new NewForumAttachment(
                    longName,
                    longPath,
                    new string('c', 80),
                    10,
                    longMime,
                    DateTimeOffset.UtcNow),
            ]);

        var stored = (await attachmentRepository.GetByLegacyPostIdsAsync([created.StarterPostId])).Single();
        Assert.Equal(255, stored.OriginalFileName.Length);
        Assert.Equal(512, stored.BlobPath.Length);
        Assert.Equal(64, stored.ContainerName.Length);
        Assert.Equal(100, stored.MimeType.Length);
    }

    [Fact]
    public async Task GetLegacyAsync_ReturnsImportedFilename()
    {
        await SeedCategoryAsync();
        dbContext.ModernForumThreads.Add(new ModernForumThreadEntity
        {
            LegacyTopicId = 50,
            LegacyForumId = 1,
            CategoryId = 1,
            Title = "Legacy attach",
            StartedByDisplayName = "Archive",
            ReplyCount = 0,
            IsSticky = false,
            IsLegacyTopicStarter = true,
            LegacyDiscography = 0,
            StarterAttachCount = 1,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
        var thread = await dbContext.ModernForumThreads.SingleAsync();
        dbContext.ModernForumPosts.Add(new ModernForumPostEntity
        {
            LegacyPostId = 77,
            LegacyThreadTopicId = 50,
            ThreadId = thread.Id,
            LegacyForumId = 1,
            AuthorDisplayName = "Archive",
            BodyHtml = "body",
            Attachment = "old-scan.jpg",
            FileSize = "12345",
            AttachCount = 1,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();

        var legacy = await attachmentRepository.GetLegacyAsync(77);

        Assert.NotNull(legacy);
        Assert.Equal("old-scan.jpg", legacy!.FileName);
        Assert.Equal(12_345L, legacy.FileSizeBytes);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task<MemberAccount> SeedMemberAsync()
    {
        var member = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = "attach@example.com",
            NormalizedEmail = "ATTACH@EXAMPLE.COM",
            DisplayName = "Attach Tester",
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.MemberAccounts.Add(member);
        await dbContext.SaveChangesAsync();
        return member;
    }

    private async Task SeedCategoryAsync()
    {
        dbContext.ModernForumCategories.Add(new ModernForumCategoryEntity
        {
            LegacyForumId = 1,
            Name = "The Music",
            SortOrder = 1,
            LegacyPostCount = 0,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    private void CreateModernForumTables()
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS ModernForumCategory
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

            CREATE TABLE IF NOT EXISTS ModernForumThread
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

            CREATE TABLE IF NOT EXISTS ModernForumPost
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
