/*
  QueenZone modern forum public read path

  Purpose
  -------
  Adds read-only stored procedures, cached read statistics, and covering
  indexes for serving the public forum archive from dbo.ModernForumCategory,
  dbo.ModernForumThread, and dbo.ModernForumPost.

  This script is safe to rerun. It creates indexes only when they are missing
  and uses CREATE OR ALTER for stored procedures.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.ModernForumCategory', N'U') IS NULL
    THROW 51000, 'dbo.ModernForumCategory does not exist. Run 004-modern-forum-batched-import.sql first.', 1;

IF OBJECT_ID(N'dbo.ModernForumThread', N'U') IS NULL
    THROW 51000, 'dbo.ModernForumThread does not exist. Run 004-modern-forum-batched-import.sql first.', 1;

IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NULL
    THROW 51000, 'dbo.ModernForumPost does not exist. Run 004-modern-forum-batched-import.sql first.', 1;
GO

IF COL_LENGTH(N'dbo.ModernForumThread', N'IsHidden') IS NULL
    ALTER TABLE dbo.ModernForumThread ADD IsHidden bit NOT NULL
        CONSTRAINT DF_ModernForumThread_IsHidden DEFAULT (0);
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ModernForumThread', N'U')
      AND name = N'IX_ModernForumThread_CategoryStarter_Latest'
)
BEGIN
    CREATE INDEX IX_ModernForumThread_CategoryStarter_Latest
    ON dbo.ModernForumThread (CategoryId, IsLegacyTopicStarter, LastActivityAt DESC, LegacyTopicId DESC)
    INCLUDE (Title);
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ModernForumThread', N'U')
      AND name = N'IX_ModernForumThread_PublicCategoryPage'
)
BEGIN
    CREATE INDEX IX_ModernForumThread_PublicCategoryPage
    ON dbo.ModernForumThread
    (
        CategoryId,
        IsLegacyTopicStarter,
        StartedByUserValidated,
        IsSticky DESC,
        LastActivityAt DESC,
        LegacyTopicId ASC
    )
    INCLUDE (Title, StartedByLegacyUserId, StartedByDisplayName, ReplyCount);
END;
GO

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.ModernForumThread', N'U')
      AND name = N'IX_ModernForumThread_Sitemap'
)
BEGIN
    CREATE INDEX IX_ModernForumThread_Sitemap
    ON dbo.ModernForumThread (LegacyTopicId ASC)
    INCLUDE (Title, LastActivityAt);
END;
GO

IF OBJECT_ID(N'dbo.ModernForumCategoryReadStats', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ModernForumCategoryReadStats
    (
        CategoryId int NOT NULL CONSTRAINT PK_ModernForumCategoryReadStats PRIMARY KEY,
        LegacyForumId int NOT NULL CONSTRAINT UQ_ModernForumCategoryReadStats_LegacyForumId UNIQUE,
        TotalThreads int NOT NULL,
        ValidatedDisplayThreads int NOT NULL,
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumCategoryReadStats_UpdatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT FK_ModernForumCategoryReadStats_Category FOREIGN KEY (CategoryId)
            REFERENCES dbo.ModernForumCategory (Id)
    );
END;
GO

IF OBJECT_ID(N'dbo.ModernForumThreadReadStats', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ModernForumThreadReadStats
    (
        ThreadId bigint NOT NULL CONSTRAINT PK_ModernForumThreadReadStats PRIMARY KEY,
        LegacyTopicId int NOT NULL CONSTRAINT UQ_ModernForumThreadReadStats_LegacyTopicId UNIQUE,
        PostCount int NOT NULL,
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumThreadReadStats_UpdatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT FK_ModernForumThreadReadStats_Thread FOREIGN KEY (ThreadId)
            REFERENCES dbo.ModernForumThread (Id)
    );
END;
GO

IF OBJECT_ID(N'dbo.ModernForumArchiveReadStats', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ModernForumArchiveReadStats
    (
        Id tinyint NOT NULL CONSTRAINT PK_ModernForumArchiveReadStats PRIMARY KEY,
        TotalThreads int NOT NULL,
        SitemapTopicCount int NOT NULL,
        UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumArchiveReadStats_UpdatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT CK_ModernForumArchiveReadStats_SingleRow CHECK (Id = 1)
    );
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_RefreshReadStats
AS
BEGIN
    SET NOCOUNT ON;

    MERGE dbo.ModernForumCategoryReadStats AS target
    USING
    (
        SELECT
            c.Id AS CategoryId,
            c.LegacyForumId,
            COUNT_BIG(CASE WHEN t.IsLegacyTopicStarter = 1 AND t.IsHidden = 0 THEN 1 END) AS TotalThreads,
            COUNT_BIG(CASE WHEN t.IsLegacyTopicStarter = 1 AND t.StartedByUserValidated = 1 AND t.IsHidden = 0 THEN 1 END) AS ValidatedDisplayThreads
        FROM dbo.ModernForumCategory c
        LEFT JOIN dbo.ModernForumThread t ON t.CategoryId = c.Id
        GROUP BY c.Id, c.LegacyForumId
    ) AS source
    ON target.CategoryId = source.CategoryId
    WHEN MATCHED THEN
        UPDATE SET
            LegacyForumId = source.LegacyForumId,
            TotalThreads = CONVERT(int, source.TotalThreads),
            ValidatedDisplayThreads = CONVERT(int, source.ValidatedDisplayThreads),
            UpdatedAt = sysutcdatetime()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (CategoryId, LegacyForumId, TotalThreads, ValidatedDisplayThreads)
        VALUES
        (
            source.CategoryId,
            source.LegacyForumId,
            CONVERT(int, source.TotalThreads),
            CONVERT(int, source.ValidatedDisplayThreads)
        )
    WHEN NOT MATCHED BY SOURCE THEN
        DELETE;

    MERGE dbo.ModernForumThreadReadStats AS target
    USING
    (
        SELECT
            t.Id AS ThreadId,
            t.LegacyTopicId,
            COUNT_BIG(CASE WHEN p.IsHidden = 0 THEN p.Id END) AS PostCount
        FROM dbo.ModernForumThread t
        LEFT JOIN dbo.ModernForumPost p ON p.ThreadId = t.Id
        GROUP BY t.Id, t.LegacyTopicId
    ) AS source
    ON target.ThreadId = source.ThreadId
    WHEN MATCHED THEN
        UPDATE SET
            LegacyTopicId = source.LegacyTopicId,
            PostCount = CONVERT(int, source.PostCount),
            UpdatedAt = sysutcdatetime()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (ThreadId, LegacyTopicId, PostCount)
        VALUES (source.ThreadId, source.LegacyTopicId, CONVERT(int, source.PostCount))
    WHEN NOT MATCHED BY SOURCE THEN
        DELETE;

    MERGE dbo.ModernForumArchiveReadStats AS target
    USING
    (
        SELECT
            CONVERT(tinyint, 1) AS Id,
            CONVERT(int, COUNT_BIG(*)) AS TotalThreads,
            CONVERT(int, COUNT_BIG(CASE WHEN NULLIF(LTRIM(RTRIM(Title)), '') IS NOT NULL THEN 1 END)) AS SitemapTopicCount
        FROM dbo.ModernForumThread
        WHERE IsHidden = 0
    ) AS source
    ON target.Id = source.Id
    WHEN MATCHED THEN
        UPDATE SET
            TotalThreads = source.TotalThreads,
            SitemapTopicCount = source.SitemapTopicCount,
            UpdatedAt = sysutcdatetime()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (Id, TotalThreads, SitemapTopicCount)
        VALUES (source.Id, source.TotalThreads, source.SitemapTopicCount);
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_GetCategories
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.LegacyForumId AS Id,
        c.Name,
        NULLIF(LTRIM(RTRIM(c.Description)), '') AS Description,
        c.LegacyPostCount AS PostCount,
        c.LastActivityAt,
        CAST(NULL AS nvarchar(200)) AS LatestThreadTitle,
        c.SortOrder
    FROM dbo.ModernForumCategory c
    WHERE c.IsSynthetic = 0
    ORDER BY c.SortOrder ASC, c.LegacyForumId ASC;
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_GetCategoryByLegacyForumId
    @Q_FORUM_ID int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        c.LegacyForumId AS Id,
        c.Name,
        NULLIF(LTRIM(RTRIM(c.Description)), '') AS Description,
        c.LegacyPostCount AS PostCount,
        c.LastActivityAt,
        latest.Title AS LatestThreadTitle,
        c.SortOrder
    FROM dbo.ModernForumCategory c
    OUTER APPLY
    (
        SELECT TOP (1) t.Title
        FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_CategoryStarter_Latest))
        WHERE t.CategoryId = c.Id
          AND t.IsLegacyTopicStarter = 1
          AND t.IsHidden = 0
        ORDER BY t.LastActivityAt DESC, t.LegacyTopicId DESC
    ) latest
    WHERE c.LegacyForumId = @Q_FORUM_ID
      AND c.IsSynthetic = 0;
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_GetCategoryThreadsPage
    @CurrentPage int,
    @PageSize int,
    @Q_FORUM_ID int,
    @TotalRecords int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Offset int = (CASE WHEN @CurrentPage > 1 THEN @CurrentPage - 1 ELSE 0 END) * @PageSize;

    SELECT @TotalRecords = s.ValidatedDisplayThreads
    FROM dbo.ModernForumCategory c
    INNER JOIN dbo.ModernForumCategoryReadStats s ON s.CategoryId = c.Id
    WHERE c.LegacyForumId = @Q_FORUM_ID
      AND c.IsSynthetic = 0;

    IF @TotalRecords IS NULL
    BEGIN
        SELECT @TotalRecords = COUNT_BIG(*)
        FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_PublicCategoryPage))
        INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
        WHERE c.LegacyForumId = @Q_FORUM_ID
          AND c.IsSynthetic = 0
          AND t.IsLegacyTopicStarter = 1
          AND t.StartedByUserValidated = 1
          AND t.IsHidden = 0;
    END;

    SELECT
        CAST(0 AS int) AS Id,
        t.LegacyTopicId AS Q_FORUM_TOPIC_ID,
        t.Title AS TOPIC_SUBJECT,
        t.LastActivityAt AS TOPIC_LAST_POST,
        t.StartedByLegacyUserId AS USER_ID,
        t.StartedByDisplayName AS USERNAME,
        t.ReplyCount AS NUMBEROFREPLIES,
        CAST(NULL AS nvarchar(100)) AS LAST_POST_USERNAME,
        CAST(CASE WHEN t.IsSticky = 1 THEN 1 ELSE 0 END AS tinyint) AS STICKY
    FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_PublicCategoryPage))
    INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
    WHERE c.LegacyForumId = @Q_FORUM_ID
      AND c.IsSynthetic = 0
      AND t.IsLegacyTopicStarter = 1
      AND t.StartedByUserValidated = 1
      AND t.IsHidden = 0
    ORDER BY t.IsSticky DESC, t.LastActivityAt DESC, t.LegacyTopicId ASC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

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
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_GetTotalThreadCount
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COALESCE(
        (SELECT TotalThreads FROM dbo.ModernForumArchiveReadStats WHERE Id = 1),
        (SELECT CONVERT(int, COUNT_BIG(*)) FROM dbo.ModernForumThread WHERE IsHidden = 0));
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_GetTopicSitemapCount
AS
BEGIN
    SET NOCOUNT ON;

    SELECT COALESCE(
        (SELECT SitemapTopicCount FROM dbo.ModernForumArchiveReadStats WHERE Id = 1),
        (SELECT CONVERT(int, COUNT_BIG(*)) FROM dbo.ModernForumThread WHERE IsHidden = 0 AND NULLIF(LTRIM(RTRIM(Title)), '') IS NOT NULL));
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_GetTopicSitemapPage
    @Offset int,
    @PageSize int
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        t.LegacyTopicId AS TopicId,
        LTRIM(RTRIM(t.Title)) AS Title,
        t.LastActivityAt
    FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_Sitemap))
    WHERE t.IsHidden = 0
      AND NULLIF(LTRIM(RTRIM(t.Title)), '') IS NOT NULL
    ORDER BY t.LegacyTopicId ASC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END;
GO

EXEC dbo.ModernForum_RefreshReadStats;
GO
