using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizePublicReadQueries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ForumIndexesSql);
            migrationBuilder.Sql("IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL EXEC(N'" + TopicPostsProcedureSql.Replace("'", "''") + "');");

            migrationBuilder.DropIndex(
                name: "IX_SearchDocument_ContentType_PublishedAt",
                table: "SearchDocument");

            migrationBuilder.CreateIndex(
                name: "IX_SearchDocument_CandidateMetadata",
                table: "SearchDocument",
                column: "Id")
                .Annotation("SqlServer:Include", new[] { "ContentType", "PublishedAt", "SourceKey" });

            migrationBuilder.CreateIndex(
                name: "IX_SearchDocument_ContentType_PublishedAt",
                table: "SearchDocument",
                columns: new[] { "ContentType", "PublishedAt" },
                descending: new[] { false, true })
                .Annotation("SqlServer:Include", new[] { "SourceKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL EXEC(N'" + PreviousTopicPostsProcedureSql.Replace("'", "''") + "');");
            migrationBuilder.Sql(RestoreForumIndexesSql);

            migrationBuilder.DropIndex(
                name: "IX_SearchDocument_CandidateMetadata",
                table: "SearchDocument");

            migrationBuilder.DropIndex(
                name: "IX_SearchDocument_ContentType_PublishedAt",
                table: "SearchDocument");

            migrationBuilder.CreateIndex(
                name: "IX_SearchDocument_ContentType_PublishedAt",
                table: "SearchDocument",
                columns: new[] { "ContentType", "PublishedAt" },
                descending: new[] { false, true });
        }

        internal const string ForumIndexesSql = """
            IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ModernForumPost') AND name = N'IX_ModernForumPost_VisibleDiscussion')
                    CREATE INDEX IX_ModernForumPost_VisibleDiscussion
                        ON dbo.ModernForumPost (LegacyThreadTopicId, PostedAt, LegacyPostId)
                        WHERE IsHidden = 0;

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ModernForumPost') AND name = N'IX_ModernForumPost_Thread_Posted')
                    CREATE INDEX IX_ModernForumPost_Thread_Posted
                        ON dbo.ModernForumPost (ThreadId, LegacyPostId)
                        INCLUDE (PostedAt, AuthorDisplayName, IsHidden)
                        WITH (DROP_EXISTING = ON);
                ELSE
                    CREATE INDEX IX_ModernForumPost_Thread_Posted
                        ON dbo.ModernForumPost (ThreadId, LegacyPostId)
                        INCLUDE (PostedAt, AuthorDisplayName, IsHidden);
            END;

            IF OBJECT_ID(N'dbo.ModernForumThread', N'U') IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ModernForumThread') AND name = N'IX_ModernForumThread_PublicRecent')
                CREATE INDEX IX_ModernForumThread_PublicRecent
                    ON dbo.ModernForumThread (LastActivityAt DESC, LegacyTopicId DESC)
                    INCLUDE (CategoryId, Title, ReplyCount)
                    WHERE IsHidden = 0 AND IsLegacyTopicStarter = 1 AND StartedByUserValidated = 1;
            """;

        internal const string TopicPostsProcedureSql = """
            CREATE OR ALTER PROCEDURE dbo.ModernForum_GetTopicPostsPage
                @CurrentPage int,
                @PageSize int,
                @Q_FORUM_TOPIC_ID int,
                @TotalRecords int OUTPUT,
                @forum_name nvarchar(100) OUTPUT,
                @SUBJECT nvarchar(200) OUTPUT,
                @Q_FORUM_ID int OUTPUT,
                @DISCO tinyint OUTPUT,
                @HasPoll bit OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @Offset int = (CASE WHEN @CurrentPage > 1 THEN @CurrentPage - 1 ELSE 0 END) * @PageSize;
                DECLARE @ThreadId bigint;

                SET @HasPoll = 0;

                SELECT
                    @ThreadId = t.Id,
                    @SUBJECT = t.Title,
                    @Q_FORUM_ID = c.LegacyForumId,
                    @forum_name = c.Name,
                    @DISCO = t.LegacyDiscography
                FROM dbo.ModernForumThread t
                INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
                WHERE t.LegacyTopicId = @Q_FORUM_TOPIC_ID
                  AND t.IsHidden = 0;

                IF @ThreadId IS NULL
                BEGIN
                    SET @TotalRecords = 0;
                    RETURN;
                END;

                IF OBJECT_ID(N'dbo.ForumPolls', N'U') IS NOT NULL
                   AND EXISTS (SELECT 1 FROM dbo.ForumPolls WHERE LegacyTopicId = @Q_FORUM_TOPIC_ID)
                BEGIN
                    SET @HasPoll = 1;
                END;

                SELECT @TotalRecords = PostCount
                FROM dbo.ModernForumThreadReadStats
                WHERE ThreadId = @ThreadId;

                -- Cached PostCount includes hidden posts (refreshed by a periodic full sweep, not
                -- incrementally on hide/unhide); fall back to an exact filtered count when absent.
                IF @TotalRecords IS NULL
                BEGIN
                    SELECT @TotalRecords = COUNT_BIG(*)
                    FROM dbo.ModernForumPost p WITH (INDEX(IX_ModernForumPost_Thread_Posted))
                    WHERE p.ThreadId = @ThreadId
                      AND p.IsHidden = 0;
                END;

                -- Resolve visible page IDs on the narrow thread index before reading body/signature LOBs.
                CREATE TABLE #PostPage (Id bigint NOT NULL PRIMARY KEY, LegacyPostId int NOT NULL);
                INSERT INTO #PostPage (Id, LegacyPostId)
                SELECT p.Id, p.LegacyPostId
                FROM dbo.ModernForumPost p WITH (INDEX(IX_ModernForumPost_Thread_Posted))
                WHERE p.ThreadId = @ThreadId AND p.IsHidden = 0
                ORDER BY p.LegacyPostId ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;

                SELECT
                    p.BodyHtml AS TOPIC_MESSAGE,
                    p.PostedAt AS TOPIC_DATE,
                    p.AuthorLegacyUserId AS USER_ID,
                    p.AuthorDisplayName AS USERNAME,
                    p.SignatureHtml AS SIGNATURE,
                    p.AuthorPostCount AS NUMBER_OF_POSTS,
                    p.AuthorJoinedAt AS DATE_CREATED,
                    p.LegacyPostId AS Q_FORUM_TOPIC_ID,
                    p.Attachment AS ATTACHMENT,
                    p.FileSize AS FILESIZE,
                    p.AttachCount AS ATTACH_COUNT,
                    CAST(0 AS tinyint) AS ONLINE,
                    CAST(NULL AS varchar(50)) AS AVATAR,
                    CAST(NULL AS varchar(30)) AS DISPLAY_MESSAGE,
                    p.LegacyDiscography AS DISCO,
                    p.AuthorMemberId,
                    p.EditedAt,
                    p.EditCount
                FROM #PostPage page
                INNER JOIN dbo.ModernForumPost p ON p.Id = page.Id
                WHERE p.IsHidden = 0
                ORDER BY page.LegacyPostId ASC;
            END;
            """;

        internal const string PreviousTopicPostsProcedureSql = """
            CREATE OR ALTER PROCEDURE dbo.ModernForum_GetTopicPostsPage
                @CurrentPage int,
                @PageSize int,
                @Q_FORUM_TOPIC_ID int,
                @TotalRecords int OUTPUT,
                @forum_name nvarchar(100) OUTPUT,
                @SUBJECT nvarchar(200) OUTPUT,
                @Q_FORUM_ID int OUTPUT,
                @DISCO tinyint OUTPUT,
                @HasPoll bit OUTPUT
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @Offset int = (CASE WHEN @CurrentPage > 1 THEN @CurrentPage - 1 ELSE 0 END) * @PageSize;
                DECLARE @ThreadId bigint;

                SET @HasPoll = 0;

                SELECT
                    @ThreadId = t.Id,
                    @SUBJECT = t.Title,
                    @Q_FORUM_ID = c.LegacyForumId,
                    @forum_name = c.Name,
                    @DISCO = t.LegacyDiscography
                FROM dbo.ModernForumThread t
                INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
                WHERE t.LegacyTopicId = @Q_FORUM_TOPIC_ID
                  AND t.IsHidden = 0;

                IF @ThreadId IS NULL
                BEGIN
                    SET @TotalRecords = 0;
                    RETURN;
                END;

                IF OBJECT_ID(N'dbo.ForumPolls', N'U') IS NOT NULL
                   AND EXISTS (SELECT 1 FROM dbo.ForumPolls WHERE LegacyTopicId = @Q_FORUM_TOPIC_ID)
                BEGIN
                    SET @HasPoll = 1;
                END;

                SELECT @TotalRecords = PostCount
                FROM dbo.ModernForumThreadReadStats
                WHERE ThreadId = @ThreadId;

                -- Cached PostCount includes hidden posts (refreshed by a periodic full sweep, not
                -- incrementally on hide/unhide); fall back to an exact filtered count when absent.
                IF @TotalRecords IS NULL
                BEGIN
                    SELECT @TotalRecords = COUNT_BIG(*)
                    FROM dbo.ModernForumPost p WITH (INDEX(IX_ModernForumPost_Thread_Posted))
                    WHERE p.ThreadId = @ThreadId
                      AND p.IsHidden = 0;
                END;

                SELECT
                    p.BodyHtml AS TOPIC_MESSAGE,
                    p.PostedAt AS TOPIC_DATE,
                    p.AuthorLegacyUserId AS USER_ID,
                    p.AuthorDisplayName AS USERNAME,
                    p.SignatureHtml AS SIGNATURE,
                    p.AuthorPostCount AS NUMBER_OF_POSTS,
                    p.AuthorJoinedAt AS DATE_CREATED,
                    p.LegacyPostId AS Q_FORUM_TOPIC_ID,
                    p.Attachment AS ATTACHMENT,
                    p.FileSize AS FILESIZE,
                    p.AttachCount AS ATTACH_COUNT,
                    CAST(0 AS tinyint) AS ONLINE,
                    CAST(NULL AS varchar(50)) AS AVATAR,
                    CAST(NULL AS varchar(30)) AS DISPLAY_MESSAGE,
                    p.LegacyDiscography AS DISCO,
                    p.AuthorMemberId,
                    p.EditedAt,
                    p.EditCount
                FROM dbo.ModernForumPost p WITH (INDEX(IX_ModernForumPost_Thread_Posted))
                WHERE p.ThreadId = @ThreadId
                  AND p.IsHidden = 0
                ORDER BY p.LegacyPostId ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            END;
            """;

        internal const string RestoreForumIndexesSql = """
            IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL
            BEGIN
                DROP INDEX IF EXISTS IX_ModernForumPost_VisibleDiscussion ON dbo.ModernForumPost;
                CREATE INDEX IX_ModernForumPost_Thread_Posted
                    ON dbo.ModernForumPost (ThreadId, LegacyPostId)
                    INCLUDE (PostedAt, AuthorDisplayName)
                    WITH (DROP_EXISTING = ON);
            END;
            IF OBJECT_ID(N'dbo.ModernForumThread', N'U') IS NOT NULL
                DROP INDEX IF EXISTS IX_ModernForumThread_PublicRecent ON dbo.ModernForumThread;
            """;
    }
}
