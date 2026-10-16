using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Exercises the production DateTimeOffset UNION ordering that SQLite cannot translate.
/// The nightly read-only mirror probe covers real legacy rows separately.
/// </summary>
public sealed class MemberPublicActivitySqlServerTests : IAsyncLifetime
{
    private readonly string databaseName = $"QueenZoneActivityTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;

    private string ServerConnectionString => Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
        ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

    private string DatabaseConnectionString => new SqlConnectionStringBuilder(ServerConnectionString)
    {
        InitialCatalog = databaseName,
    }.ConnectionString;

    public async Task InitializeAsync()
    {
        await using (var server = new SqlConnection(new SqlConnectionStringBuilder(ServerConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString))
        {
            await server.OpenAsync();
            await using var create = new SqlCommand($"CREATE DATABASE [{databaseName}]", server);
            await create.ExecuteNonQueryAsync();
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(DatabaseConnectionString).Options);

        // Only columns touched by the real read query are needed. Types match the modern EF
        // migration: submission timestamps are datetimeoffset, forum PostedAt is datetime2.
        await dbContext.Database.ExecuteSqlRawAsync("""
            CREATE TABLE dbo.MemberAccounts (Id uniqueidentifier NOT NULL PRIMARY KEY, DisplayName nvarchar(100) NOT NULL);
            CREATE TABLE dbo.ArticleSubmissions (
                Id uniqueidentifier NOT NULL PRIMARY KEY, AuthorMemberId uniqueidentifier NOT NULL,
                Title nvarchar(300) NOT NULL, Excerpt nvarchar(max) NULL, Slug nvarchar(300) NOT NULL,
                Status nvarchar(50) NOT NULL, PublishedAt datetimeoffset NULL);
            CREATE TABLE dbo.NewsSuggestions (
                Id uniqueidentifier NOT NULL PRIMARY KEY, SubmitterMemberId uniqueidentifier NOT NULL,
                Title nvarchar(max) NULL, Notes nvarchar(max) NULL, Status nvarchar(50) NOT NULL,
                SubmittedAt datetimeoffset NOT NULL, ReviewedAt datetimeoffset NULL, PromotedNewsId int NULL);
            CREATE TABLE dbo.PhotoSubmissions (
                Id uniqueidentifier NOT NULL PRIMARY KEY, SubmitterMemberId uniqueidentifier NOT NULL,
                Title nvarchar(200) NOT NULL, Description nvarchar(max) NULL, ApprovedCategory nvarchar(max) NULL,
                Status nvarchar(50) NOT NULL, SubmittedAt datetimeoffset NOT NULL, ReviewedAt datetimeoffset NULL);
            CREATE TABLE dbo.ModernForumThread (Id bigint NOT NULL PRIMARY KEY, Title nvarchar(max) NOT NULL, IsHidden bit NOT NULL DEFAULT 0);
            CREATE TABLE dbo.ModernForumPost (
                Id bigint NOT NULL PRIMARY KEY, ThreadId bigint NOT NULL, AuthorMemberId uniqueidentifier NULL, AuthorLegacyUserId int NULL,
                IsHidden bit NOT NULL, PostedAt datetime2 NULL, LegacyPostId int NOT NULL,
                LegacyThreadTopicId int NOT NULL, AuthorDisplayName nvarchar(max) NOT NULL,
                BodyHtml nvarchar(max) NOT NULL);
            """);
    }

    public async Task DisposeAsync()
    {
        if (dbContext is not null)
        {
            await dbContext.DisposeAsync();
        }

        await using var server = new SqlConnection(new SqlConnectionStringBuilder(ServerConnectionString)
        {
            InitialCatalog = "master",
        }.ConnectionString);
        await server.OpenAsync();
        await using var drop = new SqlCommand(
            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]",
            server);
        await drop.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task GetFeedPageAsync_OrdersAndPagesAllSourcesOnSqlServer()
    {
        var memberId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var baseTime = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.MemberAccounts (Id, DisplayName) VALUES ({memberId}, {"Activity Author"}), ({otherId}, {"Other Author"})");
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.ModernForumThread (Id, Title) VALUES ({1L}, {"Forum topic"})");
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.ModernForumPost
                (Id, ThreadId, AuthorMemberId, IsHidden, PostedAt, LegacyPostId, LegacyThreadTopicId, AuthorDisplayName, BodyHtml)
            VALUES ({1L}, {1L}, {memberId}, {false}, {baseTime.UtcDateTime}, {101}, {201}, {"Activity Author"}, {"Forum body"})
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.ArticleSubmissions (Id, AuthorMemberId, Title, Excerpt, Slug, Status, PublishedAt)
            VALUES ({Guid.NewGuid()}, {memberId}, {"Article title"}, {"Article excerpt"}, {"article-title"},
                {ArticleSubmissionStatus.Published}, {baseTime.AddHours(-1)})
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.NewsSuggestions (Id, SubmitterMemberId, Title, Notes, Status, SubmittedAt, ReviewedAt, PromotedNewsId)
            VALUES ({Guid.NewGuid()}, {otherId}, {"News title"}, {"News notes"}, {NewsSuggestionStatus.Promoted},
                {baseTime.AddHours(-4)}, {baseTime.AddHours(-2)}, {301})
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.PhotoSubmissions (Id, SubmitterMemberId, Title, Description, ApprovedCategory, Status, SubmittedAt, ReviewedAt)
            VALUES ({Guid.NewGuid()}, {memberId}, {"Photo title"}, {"Photo description"}, {"Concerts"},
                {PhotoSubmissionStatus.Approved}, {baseTime.AddHours(-5)}, {baseTime.AddHours(-3)})
            """);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.ArticleSubmissions (Id, AuthorMemberId, Title, Slug, Status, PublishedAt)
            VALUES ({Guid.NewGuid()}, {memberId}, {"Private draft"}, {"private-draft"},
                {ArticleSubmissionStatus.Draft}, {baseTime.AddHours(1)})
            """);

        var repository = new EfMemberPublicActivityRepository(dbContext);
        var first = await repository.GetFeedPageAsync([memberId, otherId], page: 1, pageSize: 2);
        var second = await repository.GetFeedPageAsync([memberId, otherId], page: 2, pageSize: 2);

        Assert.Equal(4, first.TotalCount);
        Assert.Equal(4, second.TotalCount);
        Assert.Equal(
            [MemberPublicActivityType.ForumPost, MemberPublicActivityType.Article],
            first.Items.Select(item => item.Type));
        Assert.Equal(
            [MemberPublicActivityType.News, MemberPublicActivityType.Photo],
            second.Items.Select(item => item.Type));
        Assert.Equal(["Activity Author", "Activity Author"], first.Items.Select(item => item.AuthorDisplayName));
        Assert.Equal(["Other Author", "Activity Author"], second.Items.Select(item => item.AuthorDisplayName));
        Assert.Equal("Forum body", first.Items[0].Summary);
        Assert.DoesNotContain(first.Items.Concat(second.Items), item => item.Title == "Private draft");
    }

    [Fact]
    public async Task GetPageAsync_FiltersHiddenParentsAndDeduplicatesIdentitiesBeforePaging()
    {
        var memberId = Guid.NewGuid();
        const int legacyId = 7001;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO dbo.MemberAccounts (Id, DisplayName) VALUES ({memberId}, {"Synthetic"})");
        // Each identity branch has a visible control, a hidden parent and a hidden post.
        // Row 7 carries both identities and must appear only once.
        for (var id = 1; id <= 7; id++)
        {
            Guid? authorId = id <= 3 || id == 7 ? memberId : null;
            int? authorLegacyId = id >= 4 ? legacyId : null;
            var time = new DateTime(2026, 1, id, 0, 0, 0, DateTimeKind.Utc);
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.ModernForumThread (Id, Title, IsHidden)
                VALUES ({(long)id}, {$"Synthetic topic {id}"}, {id == 2 || id == 5});
                INSERT INTO dbo.ModernForumPost
                    (Id, ThreadId, AuthorMemberId, AuthorLegacyUserId, IsHidden, PostedAt,
                     LegacyPostId, LegacyThreadTopicId, AuthorDisplayName, BodyHtml)
                VALUES ({(long)id}, {(long)id}, {authorId}, {authorLegacyId}, {id == 3 || id == 6},
                    {time}, {200 + id}, {100 + id}, {"Synthetic"}, {$"Synthetic body {id}"});
                """);
        }

        var repository = new EfMemberPublicActivityRepository(dbContext);
        var first = await repository.GetPageAsync(memberId, legacyId, 1, 2);
        var second = await repository.GetPageAsync(memberId, legacyId, 2, 2);
        var beyond = await repository.GetPageAsync(memberId, legacyId, 3, 2);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(3, second.TotalCount);
        Assert.Equal(3, beyond.TotalCount);
        Assert.Equal(new int?[] { 207, 204, 201 }, first.Items.Concat(second.Items).Select(item => item.ContentId));
        Assert.Equal(new[] { "Synthetic body 7", "Synthetic body 4", "Synthetic body 1" },
            first.Items.Concat(second.Items).Select(item => item.Summary));
        Assert.Empty(beyond.Items);
    }
}
