using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using QueenZone.Data;
using QueenZone.Data.Entities;

namespace QueenZone.Web.Tests;

public sealed class EfForumArchiveAuthorRepositoryTests : IAsyncDisposable
{
    private const int LegacyUserId = 5001;

    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;
    private readonly EfForumArchiveAuthorRepository repository;
    private readonly CommandCounter commandCounter = new();

    public EfForumArchiveAuthorRepositoryTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(commandCounter)
            .Options);
        dbContext.Database.EnsureCreated();
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE ModernForumThread (
                Id INTEGER PRIMARY KEY,
                LegacyTopicId INTEGER NOT NULL,
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
                UpdatedAt TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE ModernForumPost (
                Id INTEGER PRIMARY KEY,
                LegacyPostId INTEGER NOT NULL,
                LegacyThreadTopicId INTEGER NOT NULL,
                ThreadId INTEGER NOT NULL,
                LegacyForumId INTEGER NOT NULL,
                AuthorLegacyUserId INTEGER NULL,
                AuthorMemberId TEXT NULL,
                AuthorDisplayName TEXT NOT NULL,
                AuthorPostCount INTEGER NULL,
                AuthorJoinedAt TEXT NULL,
                BodyHtml TEXT NOT NULL,
                SignatureHtml TEXT NULL,
                PostedAt TEXT NULL,
                EditedAt TEXT NULL,
                EditCount INTEGER NOT NULL DEFAULT 0,
                LegacyDiscography INTEGER NOT NULL,
                AuthorUserValidated INTEGER NULL,
                Attachment TEXT NULL,
                FileSize TEXT NULL,
                AttachCount INTEGER NOT NULL,
                IsHidden INTEGER NOT NULL DEFAULT 0,
                ImportedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ThreadId) REFERENCES ModernForumThread(Id)
            );
            """);
        repository = new EfForumArchiveAuthorRepository(
            dbContext,
            legacyUserId => $"""
                SELECT
                    AuthorLegacyUserId AS LegacyUserId,
                    AuthorDisplayName AS DisplayName,
                    AuthorJoinedAt AS MemberSince,
                    (
                        SELECT COUNT(*)
                        FROM ModernForumPost counted
                        WHERE counted.AuthorLegacyUserId = {legacyUserId}
                          AND counted.IsHidden = 0
                    ) AS PostCount
                FROM ModernForumPost
                WHERE AuthorLegacyUserId = {legacyUserId}
                  AND IsHidden = 0
                ORDER BY PostedAt DESC, Id DESC
                LIMIT 1
                """);
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsNull_WhenAuthorHasNoPosts()
    {
        var result = await repository.GetSummaryAsync(LegacyUserId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSummaryAsync_UsesMostRecentDisplayName_AndExcludesHiddenFromCount()
    {
        SeedThread(1, "Thread one");
        SeedPost(1, 1, LegacyUserId, "John S Stuart", "Older post", DateTime.Parse("2010-01-01T00:00:00Z"));
        SeedPost(2, 1, LegacyUserId, "john s stuart", "Newer post, different casing", DateTime.Parse("2020-01-01T00:00:00Z"));
        SeedPost(3, 1, LegacyUserId, "john s stuart", "Hidden post", DateTime.Parse("2021-01-01T00:00:00Z"), isHidden: true);
        await dbContext.SaveChangesAsync();

        var result = await repository.GetSummaryAsync(LegacyUserId);

        Assert.NotNull(result);
        Assert.Equal("john s stuart", result!.DisplayName);
        Assert.Equal(2, result.PostCount);
    }

    [Fact]
    public async Task GetPostsPageAsync_OrdersNewestFirst_ExcludesHidden_AndPaginates()
    {
        SeedThread(1, "Golders Green Hippodrome");
        SeedThread(2, "Another thread");
        SeedPost(1, 1, LegacyUserId, "John S Stuart", "First post", DateTime.Parse("2020-01-01T00:00:00Z"));
        SeedPost(2, 1, LegacyUserId, "John S Stuart", "Second post", DateTime.Parse("2020-06-01T00:00:00Z"));
        SeedPost(3, 2, LegacyUserId, "John S Stuart", "Third post", DateTime.Parse("2021-01-01T00:00:00Z"));
        SeedPost(4, 2, LegacyUserId, "John S Stuart", "Hidden post", DateTime.Parse("2022-01-01T00:00:00Z"), isHidden: true);
        SeedPost(5, 2, 9999, "Someone else", "Different author", DateTime.Parse("2023-01-01T00:00:00Z"));
        await dbContext.SaveChangesAsync();

        var page1 = await repository.GetPostsPageAsync(LegacyUserId, page: 1, pageSize: 2, totalCount: 3);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(["Third post", "Second post"], page1.Items.Select(item => item.Summary ?? string.Empty).ToArray());

        var page2 = await repository.GetPostsPageAsync(LegacyUserId, page: 2, pageSize: 2, totalCount: 3);
        Assert.Equal(["First post"], page2.Items.Select(item => item.Summary ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task GetPostsPageAsync_UsesDescendingIdToBreakTimestampTies()
    {
        SeedThread(1, "Thread one");
        var postedAt = DateTime.Parse("2020-01-01T00:00:00Z");
        SeedPost(1, 1, LegacyUserId, "Author", "First", postedAt);
        SeedPost(2, 1, LegacyUserId, "Author", "Second", postedAt);
        SeedPost(3, 1, LegacyUserId, "Author", "Third", postedAt);
        await dbContext.SaveChangesAsync();

        var page1 = await repository.GetPostsPageAsync(LegacyUserId, 1, 2, 3);
        var page2 = await repository.GetPostsPageAsync(LegacyUserId, 2, 2, 3);

        Assert.Equal(["Third", "Second"], page1.Items.Select(item => item.Summary));
        Assert.Equal(["First"], page2.Items.Select(item => item.Summary));
    }

    [Fact]
    public async Task ArchiveAuthorPageData_PagesIdsBeforeLoadingPostBodies()
    {
        SeedThread(1, "Thread one");
        SeedPost(1, 1, LegacyUserId, "John S Stuart", "First post", DateTime.Parse("2020-01-01T00:00:00Z"));
        SeedPost(2, 1, LegacyUserId, "John S Stuart", "Second post", DateTime.Parse("2021-01-01T00:00:00Z"));
        await dbContext.SaveChangesAsync();
        commandCounter.Reset();

        var summary = await repository.GetSummaryAsync(LegacyUserId);
        Assert.NotNull(summary);
        var page = await repository.GetPostsPageAsync(
            LegacyUserId,
            page: 1,
            pageSize: 20,
            totalCount: summary!.PostCount);

        Assert.Equal(3, commandCounter.ReaderCount);
        Assert.DoesNotContain("BodyHtml", commandCounter.ReaderCommands[1], StringComparison.Ordinal);
        Assert.Contains("BodyHtml", commandCounter.ReaderCommands[2], StringComparison.Ordinal);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public void ProductionSummaryQuery_ReadsOnlyThePrecomputedTable()
    {
        var sql = EfProductionSql.CreateForumArchiveAuthorSummarySql()(LegacyUserId).Format;

        Assert.Contains("dbo.ModernForumArchiveAuthorSummary", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("ModernForumPost ", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE LegacyUserId =", sql, StringComparison.Ordinal);
    }

    private void SeedThread(long id, string title) =>
        dbContext.ModernForumThreads.Add(new ModernForumThreadEntity
        {
            Id = id,
            LegacyTopicId = (int)id,
            LegacyForumId = 1,
            CategoryId = 1,
            Title = title,
        });

    private void SeedPost(
        long id,
        long threadId,
        int authorLegacyUserId,
        string authorDisplayName,
        string bodyHtml,
        DateTime postedAt,
        bool isHidden = false) =>
        dbContext.ModernForumPosts.Add(new ModernForumPostEntity
        {
            Id = id,
            LegacyPostId = (int)id,
            LegacyThreadTopicId = (int)threadId,
            ThreadId = threadId,
            LegacyForumId = 1,
            AuthorLegacyUserId = authorLegacyUserId,
            AuthorDisplayName = authorDisplayName,
            BodyHtml = bodyHtml,
            PostedAt = postedAt,
            IsHidden = isHidden,
        });

    public async ValueTask DisposeAsync()
    {
        await dbContext.DisposeAsync();
        await connection.DisposeAsync();
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        private readonly List<string> readerCommands = [];

        public int ReaderCount => readerCommands.Count;

        public IReadOnlyList<string> ReaderCommands => readerCommands;

        public void Reset() => readerCommands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            readerCommands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
