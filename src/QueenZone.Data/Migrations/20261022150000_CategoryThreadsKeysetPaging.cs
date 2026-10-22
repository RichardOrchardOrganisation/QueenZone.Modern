using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Pages visible category topics through a covering index and a composite sort boundary.
/// No EF model changes are required for this index and procedure replacement.
/// </summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20261022150000_CategoryThreadsKeysetPaging")]
public partial class CategoryThreadsKeysetPaging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.ModernForumThread')
                  AND name = N'IX_ModernForumThread_PublicCategoryPageVisible'
            )
            BEGIN
                -- ONLINE must use dynamic SQL because SQL Express parses every CREATE INDEX (#1628).
                DECLARE @sql nvarchar(max);
                IF SERVERPROPERTY('EngineEdition') = 5
                    SET @sql = N'CREATE INDEX IX_ModernForumThread_PublicCategoryPageVisible ON dbo.ModernForumThread (CategoryId, IsLegacyTopicStarter, StartedByUserValidated, IsSticky DESC, LastActivityAt DESC, LegacyTopicId ASC) INCLUDE (Title, StartedByLegacyUserId, StartedByDisplayName, ReplyCount) WHERE IsHidden = 0 WITH (ONLINE = ON);';
                ELSE
                    SET @sql = N'CREATE INDEX IX_ModernForumThread_PublicCategoryPageVisible ON dbo.ModernForumThread (CategoryId, IsLegacyTopicStarter, StartedByUserValidated, IsSticky DESC, LastActivityAt DESC, LegacyTopicId ASC) INCLUDE (Title, StartedByLegacyUserId, StartedByDisplayName, ReplyCount) WHERE IsHidden = 0;';
                EXEC sys.sp_executesql @sql;
            END;
            """, suppressTransaction: true);

        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.ModernForum_GetCategoryThreadsPage
                @CurrentPage int, @PageSize int, @Q_FORUM_ID int, @TotalRecords int OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @Offset int = (CASE WHEN @CurrentPage > 1 THEN @CurrentPage - 1 ELSE 0 END) * @PageSize;
                SELECT @TotalRecords = s.ValidatedDisplayThreads
                FROM dbo.ModernForumCategory c
                INNER JOIN dbo.ModernForumCategoryReadStats s ON s.CategoryId = c.Id
                WHERE c.LegacyForumId = @Q_FORUM_ID AND c.IsSynthetic = 0;
                IF @TotalRecords IS NULL
                    SELECT @TotalRecords = COUNT_BIG(*) FROM dbo.ModernForumThread t
                    INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
                    WHERE c.LegacyForumId = @Q_FORUM_ID AND c.IsSynthetic = 0
                      AND t.IsLegacyTopicStarter = 1 AND t.StartedByUserValidated = 1 AND t.IsHidden = 0;
                DECLARE @CategoryId int;
                SELECT @CategoryId = Id FROM dbo.ModernForumCategory
                WHERE LegacyForumId = @Q_FORUM_ID AND IsSynthetic = 0;

                IF @Offset = 0
                BEGIN
                    SELECT TOP (@PageSize) CAST(0 AS int) AS Id, t.LegacyTopicId AS Q_FORUM_TOPIC_ID,
                        t.Title AS TOPIC_SUBJECT, t.LastActivityAt AS TOPIC_LAST_POST,
                        t.StartedByLegacyUserId AS USER_ID, t.StartedByDisplayName AS USERNAME,
                        t.ReplyCount AS NUMBEROFREPLIES, CAST(NULL AS nvarchar(100)) AS LAST_POST_USERNAME,
                        CAST(CASE WHEN t.IsSticky = 1 THEN 1 ELSE 0 END AS tinyint) AS STICKY
                    FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_PublicCategoryPageVisible))
                    WHERE t.CategoryId = @CategoryId
                      AND t.IsLegacyTopicStarter = 1 AND t.StartedByUserValidated = 1 AND t.IsHidden = 0
                    ORDER BY t.IsSticky DESC, t.LastActivityAt DESC, t.LegacyTopicId ASC;
                    RETURN;
                END;

                DECLARE @AfterSticky bit, @AfterActivityAt datetime2(7), @AfterTopicId int;
                -- Skip only covered keys; hidden rows are absent from the filtered index.
                SELECT @AfterSticky = t.IsSticky, @AfterActivityAt = t.LastActivityAt,
                    @AfterTopicId = t.LegacyTopicId
                FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_PublicCategoryPageVisible))
                WHERE t.CategoryId = @CategoryId
                  AND t.IsLegacyTopicStarter = 1 AND t.StartedByUserValidated = 1 AND t.IsHidden = 0
                ORDER BY t.IsSticky DESC, t.LastActivityAt DESC, t.LegacyTopicId ASC
                OFFSET (@Offset - 1) ROWS FETCH NEXT 1 ROWS ONLY;

                SELECT TOP (@PageSize) CAST(0 AS int) AS Id, t.LegacyTopicId AS Q_FORUM_TOPIC_ID,
                    t.Title AS TOPIC_SUBJECT, t.LastActivityAt AS TOPIC_LAST_POST,
                    t.StartedByLegacyUserId AS USER_ID, t.StartedByDisplayName AS USERNAME,
                    t.ReplyCount AS NUMBEROFREPLIES, CAST(NULL AS nvarchar(100)) AS LAST_POST_USERNAME,
                    CAST(CASE WHEN t.IsSticky = 1 THEN 1 ELSE 0 END AS tinyint) AS STICKY
                FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_PublicCategoryPageVisible))
                WHERE t.CategoryId = @CategoryId
                  AND t.IsLegacyTopicStarter = 1 AND t.StartedByUserValidated = 1 AND t.IsHidden = 0
                  AND @AfterTopicId IS NOT NULL
                  AND (
                      t.IsSticky < @AfterSticky
                      OR (t.IsSticky = @AfterSticky AND (
                          t.LastActivityAt < @AfterActivityAt
                          OR (t.LastActivityAt IS NULL AND @AfterActivityAt IS NOT NULL)
                          OR ((t.LastActivityAt = @AfterActivityAt
                               OR (t.LastActivityAt IS NULL AND @AfterActivityAt IS NULL))
                              AND t.LegacyTopicId > @AfterTopicId)
                      ))
                  )
                ORDER BY t.IsSticky DESC, t.LastActivityAt DESC, t.LegacyTopicId ASC
                -- NULL dates sort last within each sticky group; a missing boundary returns the same empty shape.
                OPTION (RECOMPILE);
            END;
            """, suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.ModernForum_GetCategoryThreadsPage
                @CurrentPage int, @PageSize int, @Q_FORUM_ID int, @TotalRecords int OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;
                DECLARE @Offset int = (CASE WHEN @CurrentPage > 1 THEN @CurrentPage - 1 ELSE 0 END) * @PageSize;
                SELECT @TotalRecords = s.ValidatedDisplayThreads
                FROM dbo.ModernForumCategory c
                INNER JOIN dbo.ModernForumCategoryReadStats s ON s.CategoryId = c.Id
                WHERE c.LegacyForumId = @Q_FORUM_ID AND c.IsSynthetic = 0;
                IF @TotalRecords IS NULL
                    SELECT @TotalRecords = COUNT_BIG(*) FROM dbo.ModernForumThread t
                    INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
                    WHERE c.LegacyForumId = @Q_FORUM_ID AND c.IsSynthetic = 0
                      AND t.IsLegacyTopicStarter = 1 AND t.StartedByUserValidated = 1 AND t.IsHidden = 0;
                SELECT CAST(0 AS int) AS Id, t.LegacyTopicId AS Q_FORUM_TOPIC_ID,
                    t.Title AS TOPIC_SUBJECT, t.LastActivityAt AS TOPIC_LAST_POST,
                    t.StartedByLegacyUserId AS USER_ID, t.StartedByDisplayName AS USERNAME,
                    t.ReplyCount AS NUMBEROFREPLIES, CAST(NULL AS nvarchar(100)) AS LAST_POST_USERNAME,
                    CAST(CASE WHEN t.IsSticky = 1 THEN 1 ELSE 0 END AS tinyint) AS STICKY
                FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_PublicCategoryPage))
                INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
                WHERE c.LegacyForumId = @Q_FORUM_ID AND c.IsSynthetic = 0
                  AND t.IsLegacyTopicStarter = 1 AND t.StartedByUserValidated = 1 AND t.IsHidden = 0
                ORDER BY t.IsSticky DESC, t.LastActivityAt DESC, t.LegacyTopicId ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            END;
            """, suppressTransaction: true);

        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS IX_ModernForumThread_PublicCategoryPageVisible ON dbo.ModernForumThread;
            """, suppressTransaction: true);
    }
}
