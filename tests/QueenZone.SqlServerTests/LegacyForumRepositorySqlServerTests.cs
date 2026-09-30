using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Runs <see cref="LegacyForumRepository"/> against production SQL and the
/// legacy <c>Q_FORUM_VIEW_PAGE_SP</c> / <c>Q_FORUM_TOPIC_NEW_SP</c> procedures
/// on a scratch SQL Server database (#1672 / #1891). Topic table and view
/// types come from QueenZoneLocal (they are not on <c>queenzone_legacy_sync</c>).
/// The read-only mirror probe is <c>EfLegacyForumRepositoryLegacyProbeTests</c>.
/// </summary>
public sealed class LegacyForumRepositorySqlServerTests : IAsyncLifetime
{
    private static readonly DateTime Day0 = new(2026, 1, 1, 12, 0, 0);
    private static readonly DateTime Day1 = new(2026, 2, 1, 12, 0, 0);
    private static readonly DateTime Day2 = new(2026, 3, 1, 12, 0, 0);
    private static readonly DateTime Day3 = new(2026, 4, 1, 12, 0, 0);
    private static readonly DateTime Day4 = new(2026, 5, 1, 12, 0, 0);
    private static readonly DateTime Day5 = new(2026, 6, 1, 12, 0, 0);

    private readonly string databaseName = $"QueenZoneLegacyForumTests_{Guid.NewGuid():N}";
    private QueenZoneDbContext dbContext = null!;
    private LegacyForumRepository repository = null!;

    private string ConnectionString
    {
        get
        {
            var source = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerTest")
                ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(source)
            {
                InitialCatalog = databaseName,
            };
            return builder.ConnectionString;
        }
    }

    public async Task InitializeAsync()
    {
        await using (var schema = new EmptySchemaContext(SchemaOptions()))
        {
            await schema.Database.EnsureCreatedAsync();
            await LegacyForumSchema.InstallAsync(schema);
        }

        dbContext = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlServer(ConnectionString).Options);
        repository = new LegacyForumRepository(dbContext);
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var schema = new EmptySchemaContext(SchemaOptions());
        await schema.Database.EnsureDeletedAsync();
        await dbContext.DisposeAsync();
    }

    [Fact]
    public void Schema_preserves_dump_column_types_and_procedure_filters()
    {
        var sql = string.Join('\n', LegacyForumSchema.InstallBatches);

        Assert.Contains("TOPIC_MESSAGE varchar(8000)", sql, StringComparison.Ordinal);
        Assert.Contains("TOPIC_SUBJECT char(75)", sql, StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT PK_Q_FORUM_TOPIC_T PRIMARY KEY NONCLUSTERED (Q_FORUM_TOPIC_ID)", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE     (TOPIC_STARTER = 1)", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE     (ACTIVE = 1)", sql, StringComparison.Ordinal);
        Assert.Contains("(TOPIC_STARTER = 1) OR", sql, StringComparison.Ordinal);
        Assert.Contains("(Q_FORUM_TOPIC_PARENT_ID = 0)", sql, StringComparison.Ordinal);
        Assert.Contains("users_t.validated = 1", sql, StringComparison.Ordinal);
        Assert.Contains("AND DISCOGRAPHY <> 2", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(Q_FORUM_MAIL_ID)", sql, StringComparison.Ordinal);
        Assert.Contains("@Q_FORUM_ID TINYINT", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Categories_order_trim_and_lookup_latest_starter_title()
    {
        var categories = await repository.GetCategoriesAsync();
        Assert.Equal([20, 1, 10], categories.Select(item => item.Id));
        Assert.Equal("Empty", categories[0].Name);
        Assert.Null(categories[0].Description);
        Assert.Equal(0, categories[0].PostCount);
        Assert.Null(categories[0].LastActivityAt);
        Assert.Null(categories[0].LatestThreadTitle);

        Assert.Equal("Alpha", categories[1].Name);
        Assert.Null(categories[1].Description);
        Assert.Equal(0, categories[1].PostCount);

        Assert.Equal("General", categories[2].Name);
        Assert.Equal("Live shows", categories[2].Description);
        Assert.Equal(100, categories[2].PostCount);
        Assert.Equal(Day2, categories[2].LastActivityAt);
        Assert.Equal(20, categories[2].SortOrder);

        var general = await repository.GetCategoryByIdAsync(10);
        Assert.NotNull(general);
        Assert.Equal("General", general.Name);
        Assert.Equal("Live shows", general.Description);
        // OUTER APPLY has no validated filter, so the newest starter wins.
        Assert.Equal("Unvalidated", general.LatestThreadTitle);
        Assert.Equal(100, general.PostCount);

        var empty = await repository.GetCategoryByIdAsync(20);
        Assert.NotNull(empty);
        Assert.Null(empty.LatestThreadTitle);

        var alpha = await repository.GetCategoryByIdAsync(1);
        Assert.NotNull(alpha);
        Assert.Equal("Alpha thread", alpha.LatestThreadTitle);

        Assert.Null(await repository.GetCategoryByIdAsync(404));
    }

    [Fact]
    public async Task Category_topics_filter_validated_order_sticky_and_page()
    {
        var page1 = await repository.GetCategoryTopicsPageAsync(10, 1, 2);
        Assert.Equal(8, page1.TotalCount);
        Assert.Equal([1000, 1005], page1.Topics.Select(topic => topic.Id));
        Assert.True(page1.Topics[0].IsSticky);
        Assert.Equal("Pinned", page1.Topics[0].Title);
        Assert.Equal("Freddie", page1.Topics[0].AuthorUsername);
        Assert.Null(page1.Topics[0].LastPostUsername);
        Assert.Equal(2, page1.Topics[0].ReplyCount);
        Assert.Equal(Day1, page1.Topics[0].LastActivityAt);
        Assert.False(page1.Topics[1].IsSticky);
        Assert.Equal(string.Empty, page1.Topics[1].Title);

        var page2 = await repository.GetCategoryTopicsPageAsync(10, 2, 2);
        Assert.Equal(8, page2.TotalCount);
        Assert.Equal([1001, 1002], page2.Topics.Select(topic => topic.Id));
        Assert.Equal("Brian", page2.Topics[0].AuthorUsername);
        Assert.Equal("Roger", page2.Topics[0].LastPostUsername);
        Assert.Equal(Day3, page2.Topics[0].LastActivityAt);

        var beyond = await repository.GetCategoryTopicsPageAsync(10, 9, 2);
        Assert.Equal(8, beyond.TotalCount);
        Assert.Empty(beyond.Topics);

        var empty = await repository.GetCategoryTopicsPageAsync(20, 1, 10);
        Assert.Equal(0, empty.TotalCount);
        Assert.Empty(empty.Topics);

        Assert.DoesNotContain(
            (await repository.GetCategoryTopicsPageAsync(10, 1, 10)).Topics,
            topic => topic.Id == 1004);
    }

    [Fact]
    public async Task Topic_posts_map_outputs_filter_disco_and_unvalidated()
    {
        var page1 = await repository.GetTopicPostsPageAsync(1000, 1, 2);
        Assert.NotNull(page1);
        Assert.Equal(1000, page1.Header.TopicId);
        Assert.Equal("Pinned", page1.Header.Title);
        Assert.Equal(10, page1.Header.ForumId);
        Assert.Equal("General", page1.Header.ForumName);
        Assert.Equal(5, page1.TotalCount);
        Assert.Equal([1000, 1006], page1.Posts.Select(post => post.Id));

        var first = page1.Posts[0];
        Assert.Equal("Hello cafe", first.Body);
        Assert.Equal("Freddie", first.AuthorUsername);
        Assert.Equal("Deacy", first.Signature);
        Assert.Equal(12, first.AuthorPostCount);
        Assert.Equal(Day1, first.AuthorMemberSince);
        Assert.Equal(Day1, first.PostedAt);
        Assert.Null(first.Attachments);

        var second = page1.Posts[1];
        Assert.Equal("reply body", second.Body);
        Assert.Equal("Brian", second.AuthorUsername);
        Assert.Null(second.Signature);
        Assert.Equal(3, second.AuthorPostCount);

        var page2 = await repository.GetTopicPostsPageAsync(1000, 2, 2);
        Assert.NotNull(page2);
        Assert.Equal([1007], page2.Posts.Select(post => post.Id));
        Assert.Equal(string.Empty, page2.Posts[0].Body);
        var attachments = page2.Posts[0].Attachments;
        Assert.NotNull(attachments);
        Assert.Equal("solo.jpg", attachments[0].FileName);
        Assert.Equal(2048L, attachments[0].FileSizeBytes);
        Assert.Equal("/forum/attachment/legacy/1007", attachments[0].Url);
        Assert.DoesNotContain(page2.Posts, post => post.Id is 1008 or 1013);

        var beyond = await repository.GetTopicPostsPageAsync(1000, 9, 10);
        Assert.NotNull(beyond);
        Assert.Equal("Pinned", beyond.Header.Title);
        Assert.Equal(5, beyond.TotalCount);
        Assert.Empty(beyond.Posts);
    }

    [Fact]
    public async Task Topic_posts_return_null_for_missing_or_blank_title()
    {
        Assert.Null(await repository.GetTopicPostsPageAsync(404, 1, 10));
        Assert.Null(await repository.GetTopicPostsPageAsync(1005, 1, 10));
    }

    [Fact]
    public async Task Archive_recent_discography_and_sitemap_materialize_legacy_types()
    {
        Assert.Equal(10, await repository.GetTotalThreadCountAsync());

        var stats = await repository.GetArchiveStatsAsync();
        Assert.Equal(3, stats.ForumCount);
        Assert.Equal(10, stats.ThreadCount);
        Assert.Equal(100, stats.PostCount);

        var recent = await repository.GetRecentThreadsAsync(3);
        Assert.Equal([1004, 1001, 1002], recent.Select(item => item.TopicId));
        Assert.Equal("Unvalidated", recent[0].Title);
        Assert.Equal(10, recent[0].CategoryId);
        Assert.Equal("General", recent[0].CategoryName);
        Assert.Equal(0, recent[0].ReplyCount);
        Assert.Equal(Day5, recent[0].LastActivityAt);
        Assert.DoesNotContain(recent, item => item.TopicId is 1005 or 1012);

        var clamped = await repository.GetRecentThreadsAsync(0);
        Assert.Equal(1004, Assert.Single(clamped).TopicId);

        var disco = await repository.GetLegacyDiscographyThreadsAsync();
        Assert.Equal([1009, 1010], disco.Select(item => item.TopicId));
        Assert.Equal("A Night At The Opera", disco[0].Title);
        Assert.Equal("Jazz", disco[1].Title);

        Assert.Equal(9, await repository.GetTopicSitemapCountAsync());
        var page = await repository.GetTopicSitemapPageAsync(0, 2);
        Assert.Equal([1000, 1001], page.Select(item => item.TopicId));
        Assert.Equal("Pinned", page[0].Title);
        Assert.Equal(Day1, page[0].LastActivityAt);

        var next = await repository.GetTopicSitemapPageAsync(2, 2);
        Assert.Equal([1002, 1003], next.Select(item => item.TopicId));
        Assert.Equal("Null sticky", next[1].Title);

        var clampedOffset = await repository.GetTopicSitemapPageAsync(-10, 1);
        Assert.Equal(1000, Assert.Single(clampedOffset).TopicId);

        Assert.Empty(await repository.GetTopicSitemapPageAsync(50, 10));
        var orphan = await repository.GetTopicSitemapPageAsync(8, 1);
        Assert.Equal(1012, Assert.Single(orphan).TopicId);
        Assert.Equal("Orphan reply", orphan[0].Title);
    }

    private async Task SeedAsync()
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO dbo.Q_FORUM_T
                (Q_FORUM_ID, Q_FORUM_NAME, Q_FORUM_DESCRIPTION, Q_FORUM_POST_COUNT, Q_FORUM_LAST_POST, FORUM_ORDER, TITLE_WORDS)
            VALUES
                (10, '  General  ', '  Live shows  ', 100, '2026-03-01T12:00:00', 20, 'General'),
                (1, 'Alpha', '   ', NULL, '2026-02-01T12:00:00', 20, 'Alpha'),
                (20, 'Empty', NULL, 0, NULL, 5, NULL);

            SET IDENTITY_INSERT dbo.USERS_T ON;
            INSERT INTO dbo.USERS_T
                (USER_ID, USERNAME, SIGNATURE, NUMBER_OF_POSTS, DATE_CREATED, VALIDATED, ONLINE_NOW, DISPLAY_MESSAGE)
            VALUES
                (1, '  Freddie  ', '  Deacy  ', 12, '2026-02-01T12:00:00', 1, 1, 'Hello'),
                (2, '  Brian  ', NULL, 3, '2026-02-01T12:00:00', 1, 0, NULL),
                (3, 'Ghost', NULL, 0, '2026-02-01T12:00:00', 0, 0, NULL),
                (4, 'Roger', NULL, 1, '2026-02-01T12:00:00', 1, 0, NULL),
                (5, 'John', NULL, 1, '2026-02-01T12:00:00', 1, 0, NULL);
            SET IDENTITY_INSERT dbo.USERS_T OFF;

            INSERT INTO dbo.Q_USERS_AVATAR_T (USER_ID, AVATAR, ACTIVE)
            VALUES (1, 'freddie.gif', 1), (2, 'brian.gif', 0);

            SET IDENTITY_INSERT dbo.Q_FORUM_TOPIC_T ON;
            INSERT INTO dbo.Q_FORUM_TOPIC_T
                (Q_FORUM_TOPIC_ID, Q_FORUM_ID, TOPIC_SUBJECT, USER_ID, TOPIC_REPLIES, TOPIC_LAST_POST, TOPIC_DATE,
                 Q_FORUM_TOPIC_PARENT_ID, TOPIC_MESSAGE, STICKY, ATTACHMENT, FILESIZE, ATTACH_COUNT,
                 LAST_USER_ID, TOPIC_STARTER, DISCOGRAPHY)
            VALUES
                (1000, 10, '  Pinned  ', 1, 2, '2026-02-01T12:00:00', '2026-02-01T12:00:00',
                 0, 'Hello cafe', 1, NULL, NULL, 0, NULL, 1, 0),
                (1001, 10, 'Tie B', 2, 0, '2026-04-01T12:00:00', '2026-03-01T12:00:00',
                 0, 'tie b', 0, NULL, NULL, 0, 4, 1, 0),
                (1002, 10, 'Tie C', 1, 5, '2026-03-01T12:00:00', '2026-03-01T12:00:00',
                 0, 'tie c', 0, NULL, NULL, 0, 4, 1, 0),
                (1003, 10, '  Null sticky  ', 1, NULL, '2026-01-01T12:00:00', '2026-01-01T12:00:00',
                 0, 'null sticky', NULL, NULL, NULL, 0, NULL, 1, 0),
                (1004, 10, 'Unvalidated', 3, 0, '2026-06-01T12:00:00', '2026-06-01T12:00:00',
                 0, 'hidden user', 0, NULL, NULL, 0, NULL, 1, 0),
                (1005, 10, '   ', 1, 0, '2026-05-01T12:00:00', '2026-05-01T12:00:00',
                 0, 'blank subject', 0, NULL, NULL, 0, NULL, 1, 0),
                (1006, 10, NULL, 2, 0, NULL, '2026-03-01T12:00:00',
                 1000, 'reply body', 0, NULL, NULL, 0, NULL, 0, 0),
                (1007, 10, NULL, 1, 0, NULL, '2026-04-01T12:00:00',
                 1000, NULL, 0, 'solo.jpg', '2048', 1, NULL, 0, 0),
                (1008, 10, NULL, 1, 0, NULL, '2026-05-01T12:00:00',
                 1000, 'hidden disco', 0, NULL, NULL, 0, NULL, 0, 2),
                (1009, 10, 'A Night At The Opera', 1, 0, '2026-01-15T12:00:00', '2026-01-15T12:00:00',
                 0, 'opera', 0, NULL, NULL, 0, NULL, 1, 1),
                (1010, 10, 'Jazz', 1, 0, '2026-01-10T12:00:00', '2026-01-10T12:00:00',
                 0, 'jazz', 0, NULL, NULL, 0, NULL, 1, 1),
                (1011, 1, 'Alpha thread', 5, 0, '2026-02-01T12:00:00', '2026-02-01T12:00:00',
                 0, 'alpha', 0, NULL, NULL, 0, NULL, 1, 0),
                (1012, 10, 'Orphan reply', 1, 0, '2026-01-01T12:00:00', '2026-01-01T12:00:00',
                 0, 'orphan', 0, NULL, NULL, 0, NULL, 0, 0),
                (1013, 10, NULL, 3, 0, NULL, '2026-06-01T12:00:00',
                 1000, 'unvalidated reply', 0, NULL, NULL, 0, NULL, 0, 0);
            SET IDENTITY_INSERT dbo.Q_FORUM_TOPIC_T OFF;
            """);
    }

    private DbContextOptions<EmptySchemaContext> SchemaOptions() =>
        new DbContextOptionsBuilder<EmptySchemaContext>().UseSqlServer(ConnectionString).Options;

    private sealed class EmptySchemaContext(DbContextOptions<EmptySchemaContext> options) : DbContext(options);
}
