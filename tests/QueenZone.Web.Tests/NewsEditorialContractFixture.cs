using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

internal sealed class NewsEditorialContractClock : TimeProvider
{
    internal static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class NewsEditorialContractFixture : IAsyncDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    internal QueenZoneDbContext Context { get; }
    internal IEditorialArticleRepository Editorial { get; }

    internal NewsEditorialContractFixture()
    {
        connection.Open();
        Context = new QueenZoneDbContext(new DbContextOptionsBuilder<QueenZoneDbContext>().UseSqlite(connection).Options);
        Context.Database.EnsureCreated();
        Editorial = new EfEditorialArticleRepository(Context, new NewsEditorialContractClock());
    }

    internal (IAdminNewsRepository Admin, INewsRepository Public) News()
    {
        AdminNewsSqliteTestHarness.EnsureNewsTable(Context);
        const string adminSelect = """
            SELECT NEWS_ID, TITLE, EXCERPT, ARTICLE, "DATE", "DATE" AS PublishedAt,
                SOURCE_URL, DISPLAY, SLUG, CREATED_AT, UPDATED_AT, EDITOR_EMAIL, USER_ID,
                NEWS_ID AS NewsId, TYPE, QUEEN_ONLINE, IMAGE_BLOB_KEY, IMAGE_GALLERY_PIC_ID, FORUM_TOPIC_ID
            FROM NEWS_T WHERE 1 = 1
            """;
        const string detail = """
            SELECT NEWS_ID AS Id, TITLE AS Title, EXCERPT AS Excerpt, ARTICLE AS Body,
                "DATE" AS PublishedAt, SOURCE_URL AS SourceUrl, DISPLAY AS IsPublished, SLUG AS Slug,
                IMAGE_BLOB_KEY AS ImageBlobKey, IMAGE_GALLERY_PIC_ID AS ImageGalleryPicId,
                FORUM_TOPIC_ID AS ForumTopicId FROM NEWS_T WHERE DISPLAY = 1
            """;
        var list = detail.Replace("ARTICLE AS Body", "CAST('' AS TEXT) AS Body", StringComparison.Ordinal);
        const string count = "SELECT COUNT(*) AS Value FROM NEWS_T WHERE DISPLAY = 1";
        const string bounds = " AND date(\"DATE\") >= date({0}) AND date(\"DATE\") < date({1})";
        var templates = new NewsRepositorySqlTemplates(
            LatestSql: list + " ORDER BY PublishedAt DESC, Id DESC LIMIT {0}",
            CountSql: count,
            ArchivePageSql: list + " ORDER BY PublishedAt DESC, Id DESC LIMIT {1} OFFSET {0}",
            ByIdSql: detail + " AND Id = {0}",
            SitemapSql: "SELECT NEWS_ID AS Id, TITLE AS Title, \"DATE\" AS PublishedAt, SLUG AS Slug FROM NEWS_T WHERE DISPLAY = 1")
        {
            ArchivePageByDecadeSql = list + bounds + " ORDER BY PublishedAt DESC, Id DESC LIMIT {3} OFFSET {2}",
            CountByDecadeSql = count + bounds,
            ArchiveYearRangeSql = "SELECT MIN(\"DATE\") AS MinPublishedAt, MAX(\"DATE\") AS MaxPublishedAt FROM NEWS_T WHERE DISPLAY = 1"
        };
        return (new EfAdminNewsRepository(Context, adminSelect, AdminNewsSqliteTestHarness.PagingSuffix), new EfNewsRepository(Context, templates));
    }

    internal IArticlesRepository Articles()
    {
        Context.Database.ExecuteSqlRaw("""
            CREATE TABLE Articles (Id INTEGER NOT NULL, Title TEXT NOT NULL, Body TEXT NOT NULL,
                PublishedAt TEXT NOT NULL, Source TEXT, CategoryName TEXT, IsPublished INTEGER NOT NULL);
            """);
        const string detail = "SELECT Id, Title, Body, PublishedAt, Source, CategoryName, IsPublished FROM Articles WHERE IsPublished = 1";
        var list = detail.Replace("Title, Body,", "Title, CAST('' AS TEXT) AS Body,", StringComparison.Ordinal);
        return new EfArticlesRepository(Context, new ArticleRepositorySqlTemplates(
            LatestSql: list + " ORDER BY PublishedAt DESC, Id DESC LIMIT {0}",
            CountSql: "SELECT COUNT(*) AS Value FROM Articles WHERE IsPublished = 1",
            ArchivePageSql: list + " ORDER BY PublishedAt DESC, Id DESC LIMIT {1} OFFSET {0}",
            ByIdSql: detail + " AND Id = {0}",
            SitemapSql: "SELECT Id, Title, PublishedAt, CAST(NULL AS TEXT) AS Slug FROM Articles WHERE IsPublished = 1")
        {
            FeedKeysSql = "SELECT Id, PublishedAt FROM Articles WHERE IsPublished = 1",
            ByIdsSql = list + " AND Id = {0}"
        }, Editorial);
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await connection.DisposeAsync();
    }
}
