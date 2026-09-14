using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfForumPostReportRepositoryTests : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private readonly QueenZoneDbContext dbContext;
    private readonly EfForumPostReportRepository repository;

    public EfForumPostReportRepositoryTests()
    {
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options);
        dbContext.Database.EnsureCreated();
        CreateModernForumTables();
        repository = new EfForumPostReportRepository(dbContext);
    }

    [Fact]
    public async Task CreateAsync_SnapshotsVisiblePostContext_AndHandlesDuplicateAndModeration()
    {
        var reporter = await SeedMemberAsync("reporter@example.test", "Reporter");
        var author = await SeedMemberAsync("author@example.test", "Reported author");
        await SeedThreadAsync(author, isHidden: false);
        await SeedPostAsync(2001, author, "Earlier context", DateTime.Parse("2026-09-14T01:00:00Z"));
        await SeedPostAsync(2002, author, "Reported <strong>evidence</strong>", DateTime.Parse("2026-09-14T02:00:00Z"));

        var createdAt = DateTimeOffset.Parse("2026-09-14T03:00:00Z");
        var first = await repository.CreateAsync(
            reporter.Id, 2002, ForumPostReportCategories.Harassment, "Details", createdAt);
        var duplicate = await repository.CreateAsync(
            reporter.Id, 2002, ForumPostReportCategories.Other, null, createdAt.AddMinutes(1));

        Assert.True(first.Succeeded);
        Assert.False(first.AlreadyReported);
        Assert.True(duplicate.AlreadyReported);
        Assert.Equal(first.ReportId, duplicate.ReportId);

        var report = await repository.GetAsync(first.ReportId!.Value);
        Assert.NotNull(report);
        Assert.Equal(author.Id, report!.ReportedMemberId);
        Assert.Equal("Reported <strong>evidence</strong>", report.PostBodySnapshot);
        Assert.Equal("Reportable thread", report.ThreadTitleSnapshot);
        Assert.Equal(createdAt, report.CreatedAt);
        Assert.Single(report.Context);
        Assert.Equal(2001, report.Context[0].PostId);
        Assert.Contains(2002, await repository.GetReportedPostIdsAsync(reporter.Id, [2001, 2002]));
        Assert.Equal(1, await repository.CountOpenAsync());

        var page = await repository.ListAsync(PrivateMessageReportStatus.Open, 1, 20);
        Assert.Single(page.Items);
        Assert.Equal("Reporter", page.Items[0].ReporterDisplayName);
        Assert.Equal("Reported author", page.Items[0].ReportedDisplayName);

        var updated = await repository.UpdateStatusAsync(
            first.ReportId.Value, PrivateMessageReportStatus.Reviewed, "admin@test.local");
        Assert.Equal(PrivateMessageReportStatus.Reviewed, updated!.Status);
        await repository.AppendViewedAuditAsync(first.ReportId.Value, "admin@test.local");
        Assert.Equal(2, await dbContext.ForumPostReportAuditLogs.CountAsync());
        Assert.Equal(0, await repository.CountOpenAsync());
    }

    [Fact]
    public async Task CreateAsync_RejectsOwnHiddenAndMissingPosts()
    {
        var author = await SeedMemberAsync("owner@example.test", "Owner");
        await SeedThreadAsync(author, isHidden: false);
        await SeedPostAsync(3001, author, "Own post", DateTime.UtcNow);
        await SeedPostAsync(3002, author, "Hidden post", DateTime.UtcNow, isHidden: true);

        var own = await repository.CreateAsync(
            author.Id, 3001, ForumPostReportCategories.Other, null, DateTimeOffset.UtcNow);
        var hidden = await repository.CreateAsync(
            Guid.NewGuid(), 3002, ForumPostReportCategories.Other, null, DateTimeOffset.UtcNow);
        var missing = await repository.CreateAsync(
            Guid.NewGuid(), int.MaxValue, ForumPostReportCategories.Other, null, DateTimeOffset.UtcNow);

        Assert.Equal(ForumPostReportText.CannotReportOwn, own.ErrorMessage);
        Assert.Equal(ForumPostReportText.PostNotFound, hidden.ErrorMessage);
        Assert.Equal(ForumPostReportText.PostNotFound, missing.ErrorMessage);
        Assert.Null(await repository.UpdateStatusAsync(Guid.NewGuid(), PrivateMessageReportStatus.Dismissed, "admin@test.local"));
    }

    [Fact]
    public async Task ResolvesMemberLinkedToLegacyPostAuthor_ForNewAndExistingReports()
    {
        var author = await SeedMemberAsync("legacy-owner@example.test", "Legacy owner");
        author.LinkedLegacyUserId = 4242;
        await dbContext.SaveChangesAsync();
        var reporter = await SeedMemberAsync("legacy-reporter@example.test", "Reporter");
        await SeedThreadAsync(author, isHidden: false);
        await SeedPostAsync(4001, author, "Legacy post", DateTime.UtcNow, authorMemberId: null, authorLegacyUserId: 4242);

        var post = await repository.GetVisiblePostAsync(4001);
        Assert.Equal(author.Id, post!.AuthorMemberId);

        var own = await repository.CreateAsync(
            author.Id, 4001, ForumPostReportCategories.Other, null, DateTimeOffset.UtcNow);
        Assert.Equal(ForumPostReportText.CannotReportOwn, own.ErrorMessage);

        var reportId = Guid.NewGuid();
        dbContext.ForumPostReports.Add(new ForumPostReportEntity
        {
            Id = reportId,
            PostId = 4001,
            TopicId = 1001,
            ReporterMemberId = reporter.Id,
            ReportedMemberId = null,
            Category = ForumPostReportCategories.Other,
            CreatedAt = DateTimeOffset.UtcNow,
            PostBodySnapshot = "Legacy post",
            AuthorDisplayNameSnapshot = "Legacy owner",
            PostCreatedAtSnapshot = DateTimeOffset.UtcNow,
            ThreadTitleSnapshot = "Reportable thread",
        });
        await dbContext.SaveChangesAsync();

        var existing = await repository.GetAsync(reportId);
        Assert.Equal(author.Id, existing!.ReportedMemberId);
    }

    private async Task<MemberAccount> SeedMemberAsync(string email, string displayName)
    {
        var member = new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.MemberAccounts.Add(member);
        await dbContext.SaveChangesAsync();
        return member;
    }

    private async Task SeedThreadAsync(MemberAccount author, bool isHidden)
    {
        var category = new ModernForumCategoryEntity
        {
            LegacyForumId = 10,
            Name = "Test board",
            SortOrder = 1,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        dbContext.ModernForumCategories.Add(category);
        await dbContext.SaveChangesAsync();
        dbContext.ModernForumThreads.Add(new ModernForumThreadEntity
        {
            LegacyTopicId = 1001,
            LegacyForumId = 10,
            CategoryId = category.Id,
            Title = "Reportable thread",
            StartedByDisplayName = author.DisplayName,
            StartedByUserValidated = true,
            IsLegacyTopicStarter = true,
            IsHidden = isHidden,
            ImportedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
    }

    private async Task SeedPostAsync(
        int postId,
        MemberAccount author,
        string body,
        DateTime postedAt,
        bool isHidden = false,
        Guid? authorMemberId = null,
        int? authorLegacyUserId = null)
    {
        var thread = await dbContext.ModernForumThreads.SingleAsync();
        dbContext.ModernForumPosts.Add(new ModernForumPostEntity
        {
            LegacyPostId = postId,
            LegacyThreadTopicId = thread.LegacyTopicId,
            ThreadId = thread.Id,
            LegacyForumId = thread.LegacyForumId,
            AuthorMemberId = authorMemberId ?? (authorLegacyUserId is null ? author.Id : null),
            AuthorLegacyUserId = authorLegacyUserId,
            AuthorDisplayName = author.DisplayName,
            AuthorUserValidated = true,
            BodyHtml = body,
            PostedAt = postedAt,
            IsHidden = isHidden,
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
                Id INTEGER PRIMARY KEY AUTOINCREMENT, LegacyForumId INTEGER NOT NULL UNIQUE,
                Name TEXT NOT NULL, Description TEXT NULL, SortOrder INTEGER NOT NULL,
                LegacyPostCount INTEGER NOT NULL, LastActivityAt TEXT NULL,
                IsSynthetic INTEGER NOT NULL DEFAULT 0, ImportedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE ModernForumThread
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, LegacyTopicId INTEGER NOT NULL UNIQUE,
                LegacyForumId INTEGER NOT NULL, CategoryId INTEGER NOT NULL, Title TEXT NOT NULL,
                StartedByLegacyUserId INTEGER NULL, StartedByDisplayName TEXT NOT NULL,
                StartedAt TEXT NULL, LastActivityAt TEXT NULL, ReplyCount INTEGER NOT NULL,
                IsSticky INTEGER NOT NULL, IsLegacyTopicStarter INTEGER NOT NULL,
                LegacyDiscography INTEGER NOT NULL, StartedByUserValidated INTEGER NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0, StarterAttachment TEXT NULL,
                StarterFileSize TEXT NULL, StarterAttachCount INTEGER NOT NULL,
                ImportedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (CategoryId) REFERENCES ModernForumCategory (Id)
            );
            CREATE TABLE ModernForumPost
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, LegacyPostId INTEGER NOT NULL UNIQUE,
                LegacyThreadTopicId INTEGER NOT NULL, ThreadId INTEGER NOT NULL,
                LegacyForumId INTEGER NOT NULL, AuthorLegacyUserId INTEGER NULL,
                AuthorDisplayName TEXT NOT NULL, AuthorPostCount INTEGER NULL,
                AuthorJoinedAt TEXT NULL, BodyHtml TEXT NOT NULL, SignatureHtml TEXT NULL,
                PostedAt TEXT NULL, LegacyDiscography INTEGER NOT NULL,
                AuthorUserValidated INTEGER NULL, Attachment TEXT NULL, FileSize TEXT NULL,
                AttachCount INTEGER NOT NULL, AuthorMemberId TEXT NULL, EditedAt TEXT NULL,
                EditCount INTEGER NOT NULL DEFAULT 0, IsHidden INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ThreadId) REFERENCES ModernForumThread (Id)
            );
            """);
    }

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }
}
