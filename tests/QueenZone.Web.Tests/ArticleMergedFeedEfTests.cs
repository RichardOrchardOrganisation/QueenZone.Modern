using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using QueenZone.Data;
using QueenZone.Data.Entities;
using QueenZone.Routing;
using QueenZone.Web;

namespace QueenZone.Web.Tests;

/// <summary>
/// Mixed community + archive hydration through real EF repositories on one
/// SQLite <see cref="QueenZoneDbContext"/>. Parallel <c>GetPublishedByIdsAsync</c>
/// on that shared context is the #322 / #335 hazard.
/// </summary>
public sealed class ArticleMergedFeedEfTests : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly QueenZoneDbContext dbContext;

    public ArticleMergedFeedEfTests()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<QueenZoneDbContext>()
            .UseSqlite(connection)
            .Options;
        dbContext = new QueenZoneDbContext(options);
        dbContext.Database.EnsureCreated();
        dbContext.Database.ExecuteSqlRaw(
            """
            CREATE TABLE Articles (
                Id INTEGER NOT NULL,
                Title TEXT NOT NULL,
                Body TEXT NOT NULL,
                PublishedAt TEXT NOT NULL,
                Source TEXT,
                CategoryName TEXT,
                IsPublished INTEGER NOT NULL
            );
            """);
    }

    public void Dispose()
    {
        dbContext.Dispose();
        connection.Dispose();
    }

    [Fact]
    public async Task Hydrate_mixed_page_through_shared_ef_context()
    {
        var authorId = Guid.NewGuid();
        dbContext.MemberAccounts.Add(new MemberAccount
        {
            Id = authorId,
            Email = "author@test.local",
            NormalizedEmail = "AUTHOR@TEST.LOCAL",
            DisplayName = "Test Author",
            CreatedAt = DateTime.UtcNow,
        });
        var communityId = Guid.NewGuid();
        dbContext.ArticleSubmissions.Add(new ArticleSubmissionEntity
        {
            Id = communityId,
            AuthorMemberId = authorId,
            Title = "Community mixed",
            Slug = "community-mixed",
            Excerpt = "Community excerpt",
            Body = "Body text for community mixed",
            WordCount = EfArticleSubmissionRepository.EstimateWordCount("Body text for community mixed"),
            Status = ArticleSubmissionStatus.Published,
            PublishedAt = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero),
        });
        dbContext.SaveChanges();
        dbContext.Database.ExecuteSqlRaw(
            """
            INSERT INTO Articles (Id, Title, Body, PublishedAt, Source, CategoryName, IsPublished)
            VALUES (10, 'Archive mixed', 'Archive body is long enough', '2020-01-01', NULL, 'Features', 1);
            """);

        const string listSelect = """
            SELECT Id, Title, CAST('' AS TEXT) AS Body, PublishedAt, Source, CategoryName, IsPublished
            FROM Articles
            WHERE IsPublished = 1
            """;
        var archive = new EfArticlesRepository(
            dbContext,
            new ArticleRepositorySqlTemplates(
                LatestSql: listSelect + " ORDER BY PublishedAt DESC, Id DESC LIMIT {0}",
                CountSql: "SELECT COUNT(*) AS Value FROM Articles WHERE IsPublished = 1",
                ArchivePageSql: listSelect + " ORDER BY PublishedAt DESC, Id DESC LIMIT {1} OFFSET {0}",
                ByIdSql: listSelect + " AND Id = {0}",
                SitemapSql: """
                SELECT Id, Title, PublishedAt, CAST(NULL AS TEXT) AS Slug
                FROM Articles WHERE IsPublished = 1
                """
            )
            {
                FeedKeysSql = "SELECT Id, PublishedAt FROM Articles WHERE IsPublished = 1",
                ByIdsSql = listSelect + " AND Id = {0}"
            });
        var community = new EfArticleRepository(dbContext);
        var cache = PublicQueryCacheServiceTests.CreateService(
            new MemoryCache(new MemoryCacheOptions()),
            articlesRepository: archive,
            communityArticleRepository: community);

        var index = await cache.GetMergedArticleFeedIndexAsync();
        Assert.Equal(2, index.Count);
        Assert.Equal(ArticleFeedSource.Community, index[0].Source);
        Assert.Equal(communityId, index[0].CommunityId);
        Assert.Equal(ArticleFeedSource.Archive, index[1].Source);
        Assert.Equal(10, index[1].ArchiveId);

        var items = await cache.HydrateArticleFeedAsync(index);

        Assert.Equal(2, items.Count);
        Assert.Equal(ArticlesRoutes.GetCommunityArticleDetailPath("community-mixed"), items[0].DetailPath);
        Assert.Equal("Community mixed", items[0].Title);
        Assert.Equal(ArticlesRoutes.GetArticleDetailPath(10, "Archive mixed"), items[1].DetailPath);
        Assert.Equal("Archive mixed", items[1].Title);
    }
}
