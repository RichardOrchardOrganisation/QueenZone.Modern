using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Storage;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

public sealed class MemberAccountDeletionPromotedMediaTests : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfMemberAccountRepository repository;

    public MemberAccountDeletionPromotedMediaTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
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

            CREATE TABLE PIC_FILES_T
            (
                PIC_ID INTEGER PRIMARY KEY,
                Url TEXT NULL,
                Thumb_URL TEXT NULL,
                Name TEXT NULL,
                DISPLAY INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE Q_STAGE_T
            (
                Q_STAGE_ID INTEGER PRIMARY KEY,
                URL TEXT NULL,
                TITLE TEXT NULL,
                PERFORMED_BY TEXT NULL,
                DESCRIPTION TEXT NULL,
                DISPLAY INTEGER NOT NULL DEFAULT 1
            );
            """);
        repository = new EfMemberAccountRepository(dbContext);
    }

    [Fact]
    public async Task PurgeDueDeletionsAsync_WhenPromotedMediaBlobDeleteThrows_StillPurgesAllDueMembers()
    {
        var first = await SeedDueMemberAsync("throw-one@example.com", "Throw One", 111, 211, "/Freddie/one.jpg", "one.mp3");
        var second = await SeedDueMemberAsync("throw-two@example.com", "Throw Two", 112, 212, "/Freddie/two.jpg", "two.mp3");
        var blobs = new ThrowingPromotedMediaBlobService();
        var service = CreateService(blobs);
        var now = new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc);

        var purged = await service.PurgeDueDeletionsAsync(now);

        Assert.Equal(2, purged);
        Assert.True(blobs.DeleteCalls >= 1);
        Assert.NotNull((await repository.FindByIdAsync(first))!.PersonalDataPurgedAt);
        Assert.NotNull((await repository.FindByIdAsync(second))!.PersonalDataPurgedAt);
        Assert.Equal(0, await CountLegacyRowsAsync("PIC_FILES_T"));
        Assert.Equal(0, await CountLegacyRowsAsync("Q_STAGE_T"));
        var queued = await repository.ListPendingDeletionBlobsAsync(50);
        Assert.Contains(queued, blob => blob is { Container: "freddie", Path: "one.jpg" });
        Assert.Contains(queued, blob => blob is { Container: "freddie", Path: "two.jpg" });
        Assert.Contains(queued, blob => blob.Container == SongFileUrl.ContainerName && blob.Path == "one.mp3");
        Assert.Contains(queued, blob => blob.Container == SongFileUrl.ContainerName && blob.Path == "two.mp3");
        Assert.False((await repository.GetDeletionProgressAsync(first))!.IsComplete);
        Assert.False((await repository.GetDeletionProgressAsync(second))!.IsComplete);
    }

    [Fact]
    public async Task DeleteImmediatelyAsync_ReturnsSuccess_WhenPromotedMediaBlobDeleteThrows()
    {
        var memberId = await SeedLiveMemberAsync("immediate-throw@example.com", "Immediate Throw", 113, 213, "/Live/now.jpg", "now.mp3");
        var service = CreateService(new ThrowingPromotedMediaBlobService());

        var result = await service.DeleteImmediatelyAsync(memberId);

        Assert.True(result.Succeeded);
        Assert.NotNull((await repository.FindByIdAsync(memberId))!.PersonalDataPurgedAt);
        Assert.Equal(0, await CountLegacyRowsAsync("PIC_FILES_T"));
        Assert.False((await repository.GetDeletionProgressAsync(memberId))!.IsComplete);
    }

    private MemberAccountService CreateService(IBlobUploadService blobs) =>
        new(
            repository,
            new InMemoryLegacyMemberLookupRepository(new Dictionary<string, LegacyMemberMatch>()),
            blobs,
            new MemberUploadQuotaService(
                new Microsoft.Extensions.Caching.Memory.MemoryCache(
                    new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                TimeProvider.System,
                Options.Create(new UploadQuotaOptions { Enabled = false })));

    private async Task<Guid> SeedDueMemberAsync(
        string email,
        string displayName,
        int picId,
        int stageId,
        string galleryPath,
        string audioFileName)
    {
        var account = await SeedLiveMemberAsync(email, displayName, picId, stageId, galleryPath, audioFileName);
        await repository.RequestDeletionAsync(
            account,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            immediate: true);
        return account;
    }

    private async Task<Guid> SeedLiveMemberAsync(
        string email,
        string displayName,
        int picId,
        int stageId,
        string galleryPath,
        string audioFileName)
    {
        var created = await repository.CreateAsync(new MemberAccount
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTime.UtcNow,
        });
        dbContext.PhotoSubmissions.Add(new PhotoSubmissionEntity
        {
            Id = Guid.NewGuid(),
            SubmitterMemberId = created.Id,
            Title = displayName,
            BlobPath = $"original/{picId}.jpg",
            WebOptimizedBlobPath = $"web/{picId}.webp",
            ThumbnailBlobPath = $"thumb/{picId}.webp",
            OriginalFileName = $"{picId}.jpg",
            MimeType = "image/jpeg",
            Status = PhotoSubmissionStatus.Approved,
            SubmittedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            PromotedPicId = picId,
        });
        dbContext.FanPerformanceSubmissions.Add(new FanPerformanceSubmissionEntity
        {
            Id = Guid.NewGuid(),
            SubmitterMemberId = created.Id,
            Title = displayName,
            CoveredSong = "Bohemian Rhapsody",
            PerformedBy = displayName,
            BlobPath = $"ugc/{stageId}.mp3",
            OriginalFileName = audioFileName,
            MimeType = "audio/mpeg",
            Status = FanPerformanceSubmissionStatus.Approved,
            SubmittedAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            RightsDeclaredAt = DateTimeOffset.Parse("2026-08-01T08:00:00Z"),
            RightsDeclarationVersion = FanPerformanceSubmissionRights.DeclarationVersion,
            PromotedStageId = stageId,
        });
        await dbContext.SaveChangesAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO PIC_FILES_T (PIC_ID, Url, Thumb_URL, Name, DISPLAY)
            VALUES ({0}, {1}, {2}, {3}, 1)
            """,
            picId,
            galleryPath,
            galleryPath.Replace(".jpg", "-thumb.webp", StringComparison.Ordinal),
            displayName);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Q_STAGE_T (Q_STAGE_ID, URL, TITLE, PERFORMED_BY, DESCRIPTION, DISPLAY)
            VALUES ({0}, {1}, {2}, {3}, {4}, 1)
            """,
            stageId,
            audioFileName,
            displayName,
            displayName,
            "A cover");
        return created.Id;
    }

    private Task<int> CountLegacyRowsAsync(string tableName) =>
        tableName switch
        {
            "PIC_FILES_T" => dbContext.Database
                .SqlQueryRaw<int>("SELECT COUNT(1) AS Value FROM PIC_FILES_T")
                .SingleAsync(),
            "Q_STAGE_T" => dbContext.Database
                .SqlQueryRaw<int>("SELECT COUNT(1) AS Value FROM Q_STAGE_T")
                .SingleAsync(),
            _ => throw new ArgumentOutOfRangeException(nameof(tableName), tableName, "Unknown legacy table."),
        };

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private sealed class ThrowingPromotedMediaBlobService : IBlobUploadService
    {
        public int DeleteCalls;

        public Task<BlobUploadResult> UploadAsync(
            Stream content,
            string originalFileName,
            string containerName,
            BlobUploadContext? context = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref DeleteCalls);
            throw new InvalidOperationException($"Simulated {containerName}/{blobName} delete failure.");
        }

        public Task<BlobContent?> OpenReadAsync(
            string containerName,
            string blobName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BlobContent?>(null);
    }
}
