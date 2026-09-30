using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QueenZone.Data;

namespace QueenZone.Web.Tests;

internal static class AdminNewsSqliteTestHarness
{
    internal const string PagingSuffix = " ORDER BY PublishedAt DESC, NewsId DESC LIMIT {1} OFFSET {0}";

    internal const string LatestNewsSql = """
        SELECT
            NEWS_ID,
            TITLE,
            EXCERPT,
            ARTICLE,
            "DATE",
            "DATE" AS PublishedAt,
            CAST(NULL AS TEXT) AS SOURCE_URL,
            DISPLAY,
            SLUG,
            CAST(NULL AS TEXT) AS CREATED_AT,
            CAST(NULL AS TEXT) AS UPDATED_AT,
            CAST(NULL AS TEXT) AS EDITOR_EMAIL,
            CAST(NULL AS INTEGER) AS USER_ID,
            NEWS_ID AS NewsId,
            TYPE,
            QUEEN_ONLINE,
            IMAGE_BLOB_KEY,
            IMAGE_GALLERY_PIC_ID,
            FORUM_TOPIC_ID
        FROM NEWS_T
        WHERE 1 = 1
        """;

    internal static void EnsureNewsTable(QueenZoneDbContext dbContext)
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS NEWS_T (
                NEWS_ID INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                TITLE TEXT NOT NULL,
                EXCERPT TEXT NOT NULL,
                ARTICLE TEXT NOT NULL,
                "DATE" TEXT NOT NULL,
                DISPLAY INTEGER NOT NULL,
                SOURCE_URL TEXT NULL,
                SLUG TEXT NULL,
                CREATED_AT TEXT NULL,
                UPDATED_AT TEXT NULL,
                EDITOR_EMAIL TEXT NULL,
                USER_ID INTEGER NULL,
                TYPE INTEGER NOT NULL DEFAULT 0,
                QUEEN_ONLINE INTEGER NOT NULL DEFAULT 0,
                IMAGE_BLOB_KEY TEXT NULL,
                IMAGE_GALLERY_PIC_ID INTEGER NULL,
                FORUM_TOPIC_ID INTEGER NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_NEWS_T_ForumTopicId
                ON NEWS_T (FORUM_TOPIC_ID)
                WHERE FORUM_TOPIC_ID IS NOT NULL;
            """);
    }

    internal static void SeedArticle(
        QueenZoneDbContext dbContext,
        int newsId,
        string title,
        string excerpt = "Excerpt",
        string body = "Body",
        string publishedAt = "2026-06-01",
        bool isPublished = false)
    {
        dbContext.Database.ExecuteSql($"""
            INSERT INTO NEWS_T (NEWS_ID, TITLE, EXCERPT, ARTICLE, "DATE", DISPLAY, TYPE, QUEEN_ONLINE)
            VALUES ({newsId}, {title}, {excerpt}, {body}, {publishedAt}, {(isPublished ? 1 : 0)}, 0, 0);
            """);
    }
}
