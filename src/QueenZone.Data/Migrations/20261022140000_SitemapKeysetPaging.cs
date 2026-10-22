using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Pages visible sitemap topics through a covering index and a legacy-topic boundary.
/// No EF model changes are required for this index and procedure replacement.
/// </summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20261022140000_SitemapKeysetPaging")]
public partial class SitemapKeysetPaging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.ModernForumThread')
                  AND name = N'IX_ModernForumThread_SitemapVisible'
            )
            BEGIN
                -- ONLINE must use dynamic SQL because SQL Express parses every CREATE INDEX (#1628).
                DECLARE @sql nvarchar(max);
                IF SERVERPROPERTY('EngineEdition') = 5
                    SET @sql = N'CREATE INDEX IX_ModernForumThread_SitemapVisible ON dbo.ModernForumThread (LegacyTopicId) INCLUDE (Title, LastActivityAt, LegacyForumId, CategoryId) WHERE IsHidden = 0 WITH (ONLINE = ON);';
                ELSE
                    SET @sql = N'CREATE INDEX IX_ModernForumThread_SitemapVisible ON dbo.ModernForumThread (LegacyTopicId) INCLUDE (Title, LastActivityAt, LegacyForumId, CategoryId) WHERE IsHidden = 0;';
                EXEC sys.sp_executesql @sql;
            END;
            """, suppressTransaction: true);

        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.ModernForum_GetTopicSitemapPage @Offset int, @PageSize int, @AfterTopicId int = NULL
            AS
            BEGIN
                SET NOCOUNT ON;

                IF @AfterTopicId IS NULL AND @Offset > 0
                BEGIN
                    SELECT @AfterTopicId = t.LegacyTopicId
                    FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_SitemapVisible))
                    INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
                    WHERE t.IsHidden = 0
                      AND NULLIF(LTRIM(RTRIM(t.Title)), '') IS NOT NULL
                      AND t.LegacyForumId > 0
                      AND c.LegacyForumId > 0
                    ORDER BY t.LegacyTopicId ASC OFFSET (@Offset - 1) ROWS FETCH NEXT 1 ROWS ONLY;
                END;

                SELECT TOP (@PageSize) t.LegacyTopicId AS TopicId, LTRIM(RTRIM(t.Title)) AS Title, t.LastActivityAt
                FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_SitemapVisible))
                INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
                WHERE t.IsHidden = 0
                  AND NULLIF(LTRIM(RTRIM(t.Title)), '') IS NOT NULL
                  AND t.LegacyForumId > 0
                  AND c.LegacyForumId > 0
                  AND (t.LegacyTopicId > @AfterTopicId OR (@AfterTopicId IS NULL AND @Offset = 0))
                ORDER BY t.LegacyTopicId ASC
                -- Fold the first-page/past-end cases so subsequent pages can seek on the boundary.
                OPTION (RECOMPILE);
            END;
            """, suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.ModernForum_GetTopicSitemapPage @Offset int, @PageSize int
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT t.LegacyTopicId AS TopicId, LTRIM(RTRIM(t.Title)) AS Title, t.LastActivityAt
                FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_Sitemap))
                INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
                WHERE t.IsHidden = 0
                  AND NULLIF(LTRIM(RTRIM(t.Title)), '') IS NOT NULL
                  AND t.LegacyForumId > 0
                  AND c.LegacyForumId > 0
                ORDER BY t.LegacyTopicId ASC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            END;
            """, suppressTransaction: true);

        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS IX_ModernForumThread_SitemapVisible ON dbo.ModernForumThread;
            """, suppressTransaction: true);
    }
}
