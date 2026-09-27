/*
QueenZone modern forum archive batched import.

Purpose:
  Create modern read-model tables for the public forum archive and import
  legacy Q_FORUM_* rows in small, resumable batches suitable for the 5 DTU
  Azure SQL tier.

Scope:
  - Creates new dbo.ModernForum* tables and dbo.ImportCheckpoint.
  - Reads legacy forum tables.
  - Does not mutate legacy Q_FORUM_* or USERS_T data.
  - Imports forum archive-shaped data only; private fields such as emails,
    passwords, IP addresses, and private messages are not copied.
  - Carries legacy visibility/moderation flags so public reads can filter
    deliberately instead of losing data during import.

Recommended 5 DTU run order:
  1. EXEC dbo.ModernForum_EnsureSchema;
  2. EXEC dbo.ModernForum_ImportCategories;
  3. EXEC dbo.ModernForum_ImportThreads @BatchSize = 250, @MaxBatches = 1;
     Repeat step 3 until RowsImported = 0.
  4. EXEC dbo.ModernForum_ImportPosts @BatchSize = 250, @MaxBatches = 1;
     Repeat step 4 until RowsImported = 0.
  5. EXEC dbo.ModernForum_ImportOrphanThreadsAndPosts;

Notes:
  - Keep @MaxBatches low on 5 DTU so each call commits quickly.
  - Lower @BatchSize to 50-100 if TOPIC_MESSAGE rows are causing timeouts.
  - The corrected full post import does not fit in a 2 GB Basic database
    alongside the legacy schema. Use a larger/separate database or scale the
    Azure SQL max size before attempting the full import.
  - Import all threads before posts. A legacy thread is any row where
    TOPIC_STARTER = 1 OR Q_FORUM_TOPIC_PARENT_ID = 0. Most legacy threads are
    self-parented TOPIC_STARTER = 1 rows, but older rows use parent_id = 0.
  - Replies are attached to imported parent threads and use the post checkpoint
    only after their thread exists.
  - After the normal import, ModernForum_ImportOrphanThreadsAndPosts recovers
    legacy rows whose parent thread row is missing or not marked as a thread
    source. It creates a recovered ModernForumThread keyed by the expected
    parent id, then imports the remaining posts.

Live run:
  First applied to queenzone-db on 2026-06-29 while the database was on the
  5 DTU tier. The initial run used the too-narrow parent_id = 0 thread rule.
  This script was then corrected to import both TOPIC_STARTER = 1 and
  parent_id = 0 thread shapes, include attachment fields, and provide
  reconciliation procedures.

  The corrected live import first hit the 2 GB Basic database size quota after
  570,000 of the 1,164,816 legacy Q_FORUM_TOPIC_T rows had been imported as
  posts. The partial ModernForum* tables were reset and the data file was
  shrunk back to 1.8 GB allocated.

  After the database max size was increased to 5 GB on 2026-06-29, this script
  completed successfully:
    - source categories/imported categories: 18 / 18
    - source threads/imported threads: 89,070 / 89,070
    - source posts/imported posts: 1,164,816 / 1,164,816
    - legacy rows not mapped to a source thread: 0
    - thread rows with starter attachments: 4,754
    - post rows with attachments: 11,690
    - data file after import: about 2.5 GB allocated of 5 GB max
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_EnsureSchema
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF OBJECT_ID(N'dbo.ImportCheckpoint', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ImportCheckpoint
        (
            Name nvarchar(100) NOT NULL CONSTRAINT PK_ImportCheckpoint PRIMARY KEY,
            LastLegacyId int NOT NULL CONSTRAINT DF_ImportCheckpoint_LastLegacyId DEFAULT (0),
            LastRunStartedAt datetime2(0) NULL,
            LastRunCompletedAt datetime2(0) NULL,
            LastRowsImported int NOT NULL CONSTRAINT DF_ImportCheckpoint_LastRowsImported DEFAULT (0),
            UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ImportCheckpoint_UpdatedAt DEFAULT (sysutcdatetime())
        );
    END;

    IF OBJECT_ID(N'dbo.ModernForumCategory', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ModernForumCategory
        (
            Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModernForumCategory PRIMARY KEY,
            LegacyForumId int NOT NULL,
            Name nvarchar(100) NOT NULL,
            Description nvarchar(400) NULL,
            SortOrder int NOT NULL,
            LegacyPostCount int NOT NULL,
            LastActivityAt datetime2(0) NULL,
            IsSynthetic bit NOT NULL CONSTRAINT DF_ModernForumCategory_IsSynthetic DEFAULT (0),
            ImportedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumCategory_ImportedAt DEFAULT (sysutcdatetime()),
            UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumCategory_UpdatedAt DEFAULT (sysutcdatetime()),
            CONSTRAINT UQ_ModernForumCategory_LegacyForumId UNIQUE (LegacyForumId)
        );
    END;

    IF OBJECT_ID(N'dbo.ModernForumThread', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ModernForumThread
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModernForumThread PRIMARY KEY,
            LegacyTopicId int NOT NULL,
            LegacyForumId int NOT NULL,
            CategoryId int NOT NULL,
            Title nvarchar(200) NOT NULL,
            StartedByLegacyUserId int NULL,
            StartedByDisplayName nvarchar(100) NOT NULL,
            StartedAt datetime2(0) NULL,
            LastActivityAt datetime2(0) NULL,
            ReplyCount int NOT NULL,
            IsSticky bit NOT NULL,
            IsLegacyTopicStarter bit NOT NULL,
            LegacyDiscography tinyint NOT NULL,
            StartedByUserValidated bit NULL,
            StarterAttachment varchar(120) NULL,
            StarterFileSize varchar(12) NULL,
            StarterAttachCount int NOT NULL,
            ImportedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumThread_ImportedAt DEFAULT (sysutcdatetime()),
            UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumThread_UpdatedAt DEFAULT (sysutcdatetime()),
            CONSTRAINT UQ_ModernForumThread_LegacyTopicId UNIQUE (LegacyTopicId),
            CONSTRAINT FK_ModernForumThread_Category FOREIGN KEY (CategoryId)
                REFERENCES dbo.ModernForumCategory (Id)
        );

        CREATE INDEX IX_ModernForumThread_Category_LastActivity
        ON dbo.ModernForumThread (CategoryId, IsSticky DESC, LastActivityAt DESC, LegacyTopicId ASC)
        INCLUDE (Title, StartedByDisplayName, ReplyCount);
    END;

    IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ModernForumPost
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModernForumPost PRIMARY KEY,
            LegacyPostId int NOT NULL,
            LegacyThreadTopicId int NOT NULL,
            ThreadId bigint NOT NULL,
            LegacyForumId int NOT NULL,
            AuthorLegacyUserId int NULL,
            AuthorDisplayName nvarchar(100) NOT NULL,
            AuthorPostCount int NULL,
            AuthorJoinedAt datetime2(0) NULL,
            BodyHtml varchar(8000) NOT NULL,
            SignatureHtml varchar(8000) NULL,
            PostedAt datetime2(0) NULL,
            LegacyDiscography tinyint NOT NULL,
            AuthorUserValidated bit NULL,
            Attachment varchar(120) NULL,
            FileSize varchar(12) NULL,
            AttachCount int NOT NULL,
            ImportedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumPost_ImportedAt DEFAULT (sysutcdatetime()),
            UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumPost_UpdatedAt DEFAULT (sysutcdatetime()),
            CONSTRAINT UQ_ModernForumPost_LegacyPostId UNIQUE (LegacyPostId),
            CONSTRAINT FK_ModernForumPost_Thread FOREIGN KEY (ThreadId)
                REFERENCES dbo.ModernForumThread (Id)
        );

        CREATE INDEX IX_ModernForumPost_Thread_Posted
        ON dbo.ModernForumPost (ThreadId, LegacyPostId ASC)
        INCLUDE (PostedAt, AuthorDisplayName);
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_ResetImport
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DROP TABLE IF EXISTS dbo.ModernForumPost;
    DROP TABLE IF EXISTS dbo.ModernForumThread;
    DROP TABLE IF EXISTS dbo.ModernForumCategory;

    IF OBJECT_ID(N'dbo.ImportCheckpoint', N'U') IS NOT NULL
    BEGIN
        DELETE FROM dbo.ImportCheckpoint
        WHERE Name IN (N'ModernForum.Threads', N'ModernForum.Posts');
    END;
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_ImportCategories
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.ModernForum_EnsureSchema;

    MERGE dbo.ModernForumCategory AS target
    USING
    (
        SELECT
            CAST(Q_FORUM_ID AS int) AS LegacyForumId,
            CONVERT(nvarchar(100), LTRIM(RTRIM(ISNULL(Q_FORUM_NAME, '')))) AS Name,
            NULLIF(CONVERT(nvarchar(400), LTRIM(RTRIM(ISNULL(Q_FORUM_DESCRIPTION, '')))), '') AS Description,
            ISNULL(CAST(FORUM_ORDER AS int), 0) AS SortOrder,
            ISNULL(Q_FORUM_POST_COUNT, 0) AS LegacyPostCount,
            CAST(Q_FORUM_LAST_POST AS datetime2(0)) AS LastActivityAt,
            CONVERT(bit, 0) AS IsSynthetic
        FROM dbo.Q_FORUM_T
        WHERE NULLIF(LTRIM(RTRIM(Q_FORUM_NAME)), '') IS NOT NULL

        UNION ALL

        SELECT
            referenced.LegacyForumId,
            CONCAT(N'Legacy Forum ', referenced.LegacyForumId) AS Name,
            N'Synthetic category for legacy forum rows that reference a missing Q_FORUM_T record.' AS Description,
            10000 + referenced.LegacyForumId AS SortOrder,
            0 AS LegacyPostCount,
            NULL AS LastActivityAt,
            CONVERT(bit, 1) AS IsSynthetic
        FROM (
            SELECT DISTINCT CAST(Q_FORUM_ID AS int) AS LegacyForumId
            FROM dbo.Q_FORUM_TOPIC_T
        ) referenced
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.Q_FORUM_T existing
            WHERE existing.Q_FORUM_ID = referenced.LegacyForumId
        )
    ) AS source
    ON target.LegacyForumId = source.LegacyForumId
    WHEN MATCHED THEN
        UPDATE SET
            Name = source.Name,
            Description = source.Description,
            SortOrder = source.SortOrder,
            LegacyPostCount = source.LegacyPostCount,
            LastActivityAt = source.LastActivityAt,
            IsSynthetic = source.IsSynthetic,
            UpdatedAt = sysutcdatetime()
    WHEN NOT MATCHED BY TARGET THEN
        INSERT (LegacyForumId, Name, Description, SortOrder, LegacyPostCount, LastActivityAt, IsSynthetic)
        VALUES (source.LegacyForumId, source.Name, source.Description, source.SortOrder, source.LegacyPostCount, source.LastActivityAt, source.IsSynthetic);
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_ImportThreads
    @BatchSize int = 250,
    @MaxBatches int = 1,
    @DelayMilliseconds int = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.ModernForum_EnsureSchema;

    SET @BatchSize = CASE WHEN @BatchSize BETWEEN 1 AND 5000 THEN @BatchSize ELSE 250 END;
    SET @MaxBatches = CASE WHEN @MaxBatches BETWEEN 1 AND 1000 THEN @MaxBatches ELSE 1 END;
    SET @DelayMilliseconds = CASE WHEN @DelayMilliseconds BETWEEN 0 AND 60000 THEN @DelayMilliseconds ELSE 0 END;

    DECLARE @checkpointName nvarchar(100) = N'ModernForum.Threads';
    DECLARE @batch int = 0;
    DECLARE @rows int = 1;
    DECLARE @totalRows int = 0;
    DECLARE @lastLegacyId int;
    DECLARE @startedAt datetime2(0);
    DECLARE @threadBatch TABLE
    (
        LegacyTopicId int NOT NULL PRIMARY KEY,
        LegacyForumId int NOT NULL,
        CategoryId int NOT NULL,
        Title nvarchar(200) NOT NULL,
        StartedByLegacyUserId int NULL,
        StartedByDisplayName nvarchar(100) NOT NULL,
        StartedAt datetime2(0) NULL,
        LastActivityAt datetime2(0) NULL,
        ReplyCount int NOT NULL,
        IsSticky bit NOT NULL,
        IsLegacyTopicStarter bit NOT NULL,
        LegacyDiscography tinyint NOT NULL,
        StartedByUserValidated bit NULL,
        StarterAttachment varchar(120) NULL,
        StarterFileSize varchar(12) NULL,
        StarterAttachCount int NOT NULL
    );

    IF NOT EXISTS (SELECT 1 FROM dbo.ImportCheckpoint WHERE Name = @checkpointName)
    BEGIN
        INSERT dbo.ImportCheckpoint (Name) VALUES (@checkpointName);
    END;

    WHILE @batch < @MaxBatches AND @rows > 0
    BEGIN
        SELECT @lastLegacyId = LastLegacyId
        FROM dbo.ImportCheckpoint WITH (UPDLOCK, HOLDLOCK)
        WHERE Name = @checkpointName;

        SET @startedAt = sysutcdatetime();
        DELETE FROM @threadBatch;

        INSERT @threadBatch
        (
            LegacyTopicId,
            LegacyForumId,
            CategoryId,
            Title,
            StartedByLegacyUserId,
            StartedByDisplayName,
            StartedAt,
            LastActivityAt,
            ReplyCount,
            IsSticky,
            IsLegacyTopicStarter,
            LegacyDiscography,
            StartedByUserValidated,
            StarterAttachment,
            StarterFileSize,
            StarterAttachCount
        )
        SELECT TOP (@BatchSize)
            t.Q_FORUM_TOPIC_ID,
            CAST(t.Q_FORUM_ID AS int),
            c.Id,
            COALESCE(NULLIF(CONVERT(nvarchar(200), LTRIM(RTRIM(t.TOPIC_SUBJECT))), N''), N'(untitled)'),
            t.USER_ID,
            COALESCE(NULLIF(CONVERT(nvarchar(100), LTRIM(RTRIM(u.USERNAME))), ''), N'Unknown'),
            CAST(t.TOPIC_DATE AS datetime2(0)),
            CAST(t.TOPIC_LAST_POST AS datetime2(0)),
            ISNULL(CAST(t.TOPIC_REPLIES AS int), 0),
            CASE WHEN ISNULL(t.STICKY, 0) = 1 THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END,
            CASE WHEN ISNULL(t.TOPIC_STARTER, 0) = 1 THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END,
            ISNULL(t.DISCOGRAPHY, 0),
            CASE WHEN u.USER_ID IS NULL THEN NULL WHEN ISNULL(u.VALIDATED, 0) = 1 THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END,
            NULLIF(t.ATTACHMENT, ''),
            NULLIF(CONVERT(varchar(12), LTRIM(RTRIM(ISNULL(t.FILESIZE, '')))), ''),
            ISNULL(CAST(t.ATTACH_COUNT AS int), 0)
        FROM dbo.Q_FORUM_TOPIC_T t
        INNER JOIN dbo.ModernForumCategory c
            ON c.LegacyForumId = CAST(t.Q_FORUM_ID AS int)
        LEFT JOIN dbo.USERS_T u
            ON u.USER_ID = t.USER_ID
        WHERE t.Q_FORUM_TOPIC_ID > @lastLegacyId
          AND (t.TOPIC_STARTER = 1 OR t.Q_FORUM_TOPIC_PARENT_ID = 0)
        ORDER BY t.Q_FORUM_TOPIC_ID ASC;

        BEGIN TRANSACTION;

        INSERT dbo.ModernForumThread
        (
            LegacyTopicId,
            LegacyForumId,
            CategoryId,
            Title,
            StartedByLegacyUserId,
            StartedByDisplayName,
            StartedAt,
            LastActivityAt,
            ReplyCount,
            IsSticky,
            IsLegacyTopicStarter,
            LegacyDiscography,
            StartedByUserValidated,
            StarterAttachment,
            StarterFileSize,
            StarterAttachCount
        )
        SELECT
            b.LegacyTopicId,
            b.LegacyForumId,
            b.CategoryId,
            b.Title,
            b.StartedByLegacyUserId,
            b.StartedByDisplayName,
            b.StartedAt,
            b.LastActivityAt,
            b.ReplyCount,
            b.IsSticky,
            b.IsLegacyTopicStarter,
            b.LegacyDiscography,
            b.StartedByUserValidated,
            b.StarterAttachment,
            b.StarterFileSize,
            b.StarterAttachCount
        FROM @threadBatch b
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.ModernForumThread existing
            WHERE existing.LegacyTopicId = b.LegacyTopicId
        );

        SET @rows = @@ROWCOUNT;

        UPDATE dbo.ImportCheckpoint
        SET
            LastLegacyId = ISNULL((SELECT MAX(LegacyTopicId) FROM @threadBatch), LastLegacyId),
            LastRunStartedAt = @startedAt,
            LastRunCompletedAt = sysutcdatetime(),
            LastRowsImported = @rows,
            UpdatedAt = sysutcdatetime()
        WHERE Name = @checkpointName;

        COMMIT TRANSACTION;

        SET @totalRows += @rows;
        SET @batch += 1;

        IF @rows > 0 AND @DelayMilliseconds > 0
        BEGIN
            DECLARE @delay varchar(12) = CONVERT(varchar(12), DATEADD(millisecond, @DelayMilliseconds, CONVERT(time(3), '00:00:00.000')), 114);
            WAITFOR DELAY @delay;
        END;
    END;

    SELECT @totalRows AS RowsImported, @batch AS BatchesRun;
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_ImportPosts
    @BatchSize int = 250,
    @MaxBatches int = 1,
    @DelayMilliseconds int = 0
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.ModernForum_EnsureSchema;

    IF NOT EXISTS (
        SELECT 1
        FROM dbo.ImportCheckpoint
        WHERE Name = N'ModernForum.Threads'
          AND LastRowsImported = 0
    )
    BEGIN
        RAISERROR('ModernForum_ImportPosts requires ModernForum_ImportThreads to finish first. Repeat thread imports until RowsImported = 0, then run posts.', 16, 1);
        RETURN;
    END;

    IF EXISTS (
        SELECT 1
        FROM dbo.ImportCheckpoint
        WHERE Name = N'ModernForum.Threads'
          AND LastRowsImported <> 0
    )
    BEGIN
        RAISERROR('ModernForum_ImportPosts requires ModernForum_ImportThreads to finish first. Repeat thread imports until RowsImported = 0, then run posts.', 16, 1);
        RETURN;
    END;

    SET @BatchSize = CASE WHEN @BatchSize BETWEEN 1 AND 5000 THEN @BatchSize ELSE 250 END;
    SET @MaxBatches = CASE WHEN @MaxBatches BETWEEN 1 AND 1000 THEN @MaxBatches ELSE 1 END;
    SET @DelayMilliseconds = CASE WHEN @DelayMilliseconds BETWEEN 0 AND 60000 THEN @DelayMilliseconds ELSE 0 END;

    DECLARE @checkpointName nvarchar(100) = N'ModernForum.Posts';
    DECLARE @batch int = 0;
    DECLARE @rows int = 1;
    DECLARE @totalRows int = 0;
    DECLARE @lastLegacyId int;
    DECLARE @startedAt datetime2(0);
    DECLARE @postBatch TABLE
    (
        LegacyPostId int NOT NULL PRIMARY KEY,
        LegacyThreadTopicId int NOT NULL,
        ThreadId bigint NOT NULL,
        LegacyForumId int NOT NULL,
        AuthorLegacyUserId int NULL,
        AuthorDisplayName nvarchar(100) NOT NULL,
        AuthorPostCount int NULL,
        AuthorJoinedAt datetime2(0) NULL,
        BodyHtml varchar(8000) NOT NULL,
        SignatureHtml varchar(8000) NULL,
        PostedAt datetime2(0) NULL,
        LegacyDiscography tinyint NOT NULL,
        AuthorUserValidated bit NULL,
        Attachment varchar(120) NULL,
        FileSize varchar(12) NULL,
        AttachCount int NOT NULL
    );

    IF NOT EXISTS (SELECT 1 FROM dbo.ImportCheckpoint WHERE Name = @checkpointName)
    BEGIN
        INSERT dbo.ImportCheckpoint (Name) VALUES (@checkpointName);
    END;

    WHILE @batch < @MaxBatches AND @rows > 0
    BEGIN
        SELECT @lastLegacyId = LastLegacyId
        FROM dbo.ImportCheckpoint WITH (UPDLOCK, HOLDLOCK)
        WHERE Name = @checkpointName;

        SET @startedAt = sysutcdatetime();
        DELETE FROM @postBatch;

        INSERT @postBatch
        (
            LegacyPostId,
            LegacyThreadTopicId,
            ThreadId,
            LegacyForumId,
            AuthorLegacyUserId,
            AuthorDisplayName,
            AuthorPostCount,
            AuthorJoinedAt,
            BodyHtml,
            SignatureHtml,
            PostedAt,
            LegacyDiscography,
            AuthorUserValidated,
            Attachment,
            FileSize,
            AttachCount
        )
        SELECT TOP (@BatchSize)
            t.Q_FORUM_TOPIC_ID,
            mt.LegacyTopicId,
            mt.Id,
            CAST(t.Q_FORUM_ID AS int),
            t.USER_ID,
            COALESCE(NULLIF(CONVERT(nvarchar(100), LTRIM(RTRIM(u.USERNAME))), ''), N'Unknown'),
            CAST(u.NUMBER_OF_POSTS AS int),
            CAST(u.DATE_CREATED AS datetime2(0)),
            ISNULL(t.TOPIC_MESSAGE, ''),
            NULLIF(u.SIGNATURE, ''),
            CAST(t.TOPIC_DATE AS datetime2(0)),
            ISNULL(t.DISCOGRAPHY, 0),
            CASE WHEN u.USER_ID IS NULL THEN NULL WHEN ISNULL(u.VALIDATED, 0) = 1 THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END,
            NULLIF(t.ATTACHMENT, ''),
            NULLIF(CONVERT(varchar(12), LTRIM(RTRIM(ISNULL(t.FILESIZE, '')))), ''),
            ISNULL(CAST(t.ATTACH_COUNT AS int), 0)
        FROM dbo.Q_FORUM_TOPIC_T t
        INNER JOIN dbo.ModernForumThread mt
            ON mt.LegacyTopicId = CASE
                WHEN t.TOPIC_STARTER = 1 OR t.Q_FORUM_TOPIC_PARENT_ID = 0 THEN t.Q_FORUM_TOPIC_ID
                ELSE t.Q_FORUM_TOPIC_PARENT_ID
            END
        LEFT JOIN dbo.USERS_T u
            ON u.USER_ID = t.USER_ID
        WHERE t.Q_FORUM_TOPIC_ID > @lastLegacyId
        ORDER BY t.Q_FORUM_TOPIC_ID ASC;

        BEGIN TRANSACTION;

        INSERT dbo.ModernForumPost
        (
            LegacyPostId,
            LegacyThreadTopicId,
            ThreadId,
            LegacyForumId,
            AuthorLegacyUserId,
            AuthorDisplayName,
            AuthorPostCount,
            AuthorJoinedAt,
            BodyHtml,
            SignatureHtml,
            PostedAt,
            LegacyDiscography,
            AuthorUserValidated,
            Attachment,
            FileSize,
            AttachCount
        )
        SELECT
            b.LegacyPostId,
            b.LegacyThreadTopicId,
            b.ThreadId,
            b.LegacyForumId,
            b.AuthorLegacyUserId,
            b.AuthorDisplayName,
            b.AuthorPostCount,
            b.AuthorJoinedAt,
            b.BodyHtml,
            b.SignatureHtml,
            b.PostedAt,
            b.LegacyDiscography,
            b.AuthorUserValidated,
            b.Attachment,
            b.FileSize,
            b.AttachCount
        FROM @postBatch b
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.ModernForumPost existing
            WHERE existing.LegacyPostId = b.LegacyPostId
        );

        SET @rows = @@ROWCOUNT;

        UPDATE dbo.ImportCheckpoint
        SET
            LastLegacyId = ISNULL((SELECT MAX(LegacyPostId) FROM @postBatch), LastLegacyId),
            LastRunStartedAt = @startedAt,
            LastRunCompletedAt = sysutcdatetime(),
            LastRowsImported = @rows,
            UpdatedAt = sysutcdatetime()
        WHERE Name = @checkpointName;

        COMMIT TRANSACTION;

        SET @totalRows += @rows;
        SET @batch += 1;

        IF @rows > 0 AND @DelayMilliseconds > 0
        BEGIN
            DECLARE @delay varchar(12) = CONVERT(varchar(12), DATEADD(millisecond, @DelayMilliseconds, CONVERT(time(3), '00:00:00.000')), 114);
            WAITFOR DELAY @delay;
        END;
    END;

    SELECT @totalRows AS RowsImported, @batch AS BatchesRun;
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_ImportOrphanThreadsAndPosts
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    EXEC dbo.ModernForum_EnsureSchema;

    WITH ExpectedPostThread AS
    (
        SELECT
            p.Q_FORUM_TOPIC_ID,
            CASE
                WHEN p.TOPIC_STARTER = 1 OR p.Q_FORUM_TOPIC_PARENT_ID = 0 THEN p.Q_FORUM_TOPIC_ID
                ELSE p.Q_FORUM_TOPIC_PARENT_ID
            END AS ExpectedThreadTopicId
        FROM dbo.Q_FORUM_TOPIC_T p
    ),
    MissingThread AS
    (
        SELECT DISTINCT ExpectedThreadTopicId
        FROM ExpectedPostThread expected
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.ModernForumThread existing
            WHERE existing.LegacyTopicId = expected.ExpectedThreadTopicId
        )
    ),
    MissingThreadDetail AS
    (
        SELECT
            missing.ExpectedThreadTopicId,
            COALESCE(parent.Q_FORUM_ID, representative.Q_FORUM_ID) AS LegacyForumId,
            COALESCE(parent.TOPIC_SUBJECT, representative.TOPIC_SUBJECT) AS Title,
            COALESCE(parent.USER_ID, representative.USER_ID) AS UserId,
            COALESCE(parent.TOPIC_DATE, representative.FirstPostAt) AS StartedAt,
            COALESCE(parent.TOPIC_LAST_POST, representative.LastPostAt) AS LastActivityAt,
            ISNULL(parent.TOPIC_REPLIES, representative.ChildRows) AS ReplyCount,
            ISNULL(parent.STICKY, 0) AS Sticky,
            ISNULL(parent.TOPIC_STARTER, 0) AS TopicStarter,
            ISNULL(parent.DISCOGRAPHY, representative.Discography) AS Discography,
            parent.ATTACHMENT,
            parent.FILESIZE,
            ISNULL(parent.ATTACH_COUNT, 0) AS AttachCount
        FROM MissingThread missing
        LEFT JOIN dbo.Q_FORUM_TOPIC_T parent
            ON parent.Q_FORUM_TOPIC_ID = missing.ExpectedThreadTopicId
        OUTER APPLY (
            SELECT TOP 1
                child.Q_FORUM_ID,
                child.TOPIC_SUBJECT,
                child.USER_ID,
                child.DISCOGRAPHY,
                MIN(child.TOPIC_DATE) OVER (PARTITION BY child.Q_FORUM_TOPIC_PARENT_ID) AS FirstPostAt,
                MAX(child.TOPIC_DATE) OVER (PARTITION BY child.Q_FORUM_TOPIC_PARENT_ID) AS LastPostAt,
                COUNT(*) OVER (PARTITION BY child.Q_FORUM_TOPIC_PARENT_ID) AS ChildRows
            FROM dbo.Q_FORUM_TOPIC_T child
            WHERE child.Q_FORUM_TOPIC_PARENT_ID = missing.ExpectedThreadTopicId
            ORDER BY child.Q_FORUM_TOPIC_ID ASC
        ) representative
    )
    INSERT dbo.ModernForumThread
    (
        LegacyTopicId,
        LegacyForumId,
        CategoryId,
        Title,
        StartedByLegacyUserId,
        StartedByDisplayName,
        StartedAt,
        LastActivityAt,
        ReplyCount,
        IsSticky,
        IsLegacyTopicStarter,
        LegacyDiscography,
        StartedByUserValidated,
        StarterAttachment,
        StarterFileSize,
        StarterAttachCount
    )
    SELECT
        missing.ExpectedThreadTopicId,
        CAST(missing.LegacyForumId AS int),
        category.Id,
        COALESCE(
            NULLIF(CONVERT(nvarchar(200), LTRIM(RTRIM(missing.Title))), N''),
            CONCAT(N'Recovered legacy thread ', missing.ExpectedThreadTopicId)),
        missing.UserId,
        COALESCE(NULLIF(CONVERT(nvarchar(100), LTRIM(RTRIM(users.USERNAME))), ''), N'Unknown'),
        CAST(missing.StartedAt AS datetime2(0)),
        CAST(missing.LastActivityAt AS datetime2(0)),
        ISNULL(CAST(missing.ReplyCount AS int), 0),
        CASE WHEN ISNULL(missing.Sticky, 0) = 1 THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END,
        CASE WHEN ISNULL(missing.TopicStarter, 0) = 1 THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END,
        ISNULL(missing.Discography, 0),
        CASE WHEN users.USER_ID IS NULL THEN NULL WHEN ISNULL(users.VALIDATED, 0) = 1 THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END,
        NULLIF(missing.ATTACHMENT, ''),
        NULLIF(CONVERT(varchar(12), LTRIM(RTRIM(ISNULL(missing.FILESIZE, '')))), ''),
        ISNULL(CAST(missing.AttachCount AS int), 0)
    FROM MissingThreadDetail missing
    INNER JOIN dbo.ModernForumCategory category
        ON category.LegacyForumId = CAST(missing.LegacyForumId AS int)
    LEFT JOIN dbo.USERS_T users
        ON users.USER_ID = missing.UserId
    WHERE missing.LegacyForumId IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM dbo.ModernForumThread existing
        WHERE existing.LegacyTopicId = missing.ExpectedThreadTopicId
    );

    INSERT dbo.ModernForumPost
    (
        LegacyPostId,
        LegacyThreadTopicId,
        ThreadId,
        LegacyForumId,
        AuthorLegacyUserId,
        AuthorDisplayName,
        AuthorPostCount,
        AuthorJoinedAt,
        BodyHtml,
        SignatureHtml,
        PostedAt,
        LegacyDiscography,
        AuthorUserValidated,
        Attachment,
        FileSize,
        AttachCount
    )
    SELECT
        p.Q_FORUM_TOPIC_ID,
        mt.LegacyTopicId,
        mt.Id,
        CAST(p.Q_FORUM_ID AS int),
        p.USER_ID,
        COALESCE(NULLIF(CONVERT(nvarchar(100), LTRIM(RTRIM(users.USERNAME))), ''), N'Unknown'),
        CAST(users.NUMBER_OF_POSTS AS int),
        CAST(users.DATE_CREATED AS datetime2(0)),
        ISNULL(p.TOPIC_MESSAGE, ''),
        NULLIF(users.SIGNATURE, ''),
        CAST(p.TOPIC_DATE AS datetime2(0)),
        ISNULL(p.DISCOGRAPHY, 0),
        CASE WHEN users.USER_ID IS NULL THEN NULL WHEN ISNULL(users.VALIDATED, 0) = 1 THEN CONVERT(bit, 1) ELSE CONVERT(bit, 0) END,
        NULLIF(p.ATTACHMENT, ''),
        NULLIF(CONVERT(varchar(12), LTRIM(RTRIM(ISNULL(p.FILESIZE, '')))), ''),
        ISNULL(CAST(p.ATTACH_COUNT AS int), 0)
    FROM dbo.Q_FORUM_TOPIC_T p
    INNER JOIN dbo.ModernForumThread mt
        ON mt.LegacyTopicId = CASE
            WHEN p.TOPIC_STARTER = 1 OR p.Q_FORUM_TOPIC_PARENT_ID = 0 THEN p.Q_FORUM_TOPIC_ID
            ELSE p.Q_FORUM_TOPIC_PARENT_ID
        END
    LEFT JOIN dbo.USERS_T users
        ON users.USER_ID = p.USER_ID
    WHERE NOT EXISTS (
        SELECT 1
        FROM dbo.ModernForumPost existing
        WHERE existing.LegacyPostId = p.Q_FORUM_TOPIC_ID
    );

    SELECT
        (SELECT COUNT(*) FROM dbo.ModernForumThread) AS Threads,
        (SELECT COUNT(*) FROM dbo.ModernForumPost) AS Posts;
END;
GO

CREATE OR ALTER PROCEDURE dbo.ModernForum_GetImportReconciliation
AS
BEGIN
    SET NOCOUNT ON;

    WITH CategorySource AS
    (
        SELECT CAST(Q_FORUM_ID AS int) AS LegacyForumId
        FROM dbo.Q_FORUM_T
        WHERE NULLIF(LTRIM(RTRIM(Q_FORUM_NAME)), '') IS NOT NULL

        UNION

        SELECT DISTINCT CAST(Q_FORUM_ID AS int) AS LegacyForumId
        FROM dbo.Q_FORUM_TOPIC_T
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.Q_FORUM_T existing
            WHERE existing.Q_FORUM_ID = CAST(Q_FORUM_TOPIC_T.Q_FORUM_ID AS int)
        )
    ),
    ThreadSource AS
    (
        SELECT Q_FORUM_TOPIC_ID
        FROM dbo.Q_FORUM_TOPIC_T
        WHERE TOPIC_STARTER = 1
           OR Q_FORUM_TOPIC_PARENT_ID = 0

        UNION

        SELECT DISTINCT
            CASE
                WHEN p.TOPIC_STARTER = 1 OR p.Q_FORUM_TOPIC_PARENT_ID = 0 THEN p.Q_FORUM_TOPIC_ID
                ELSE p.Q_FORUM_TOPIC_PARENT_ID
            END AS Q_FORUM_TOPIC_ID
        FROM dbo.Q_FORUM_TOPIC_T p
        WHERE NOT EXISTS (
            SELECT 1
            FROM dbo.Q_FORUM_TOPIC_T normalThread
            WHERE normalThread.Q_FORUM_TOPIC_ID = CASE
                WHEN p.TOPIC_STARTER = 1 OR p.Q_FORUM_TOPIC_PARENT_ID = 0 THEN p.Q_FORUM_TOPIC_ID
                ELSE p.Q_FORUM_TOPIC_PARENT_ID
            END
              AND (normalThread.TOPIC_STARTER = 1 OR normalThread.Q_FORUM_TOPIC_PARENT_ID = 0)
        )
    ),
    PostSource AS
    (
        SELECT p.Q_FORUM_TOPIC_ID
        FROM dbo.Q_FORUM_TOPIC_T p
        INNER JOIN ThreadSource threads
            ON threads.Q_FORUM_TOPIC_ID = CASE
                WHEN p.TOPIC_STARTER = 1 OR p.Q_FORUM_TOPIC_PARENT_ID = 0 THEN p.Q_FORUM_TOPIC_ID
                ELSE p.Q_FORUM_TOPIC_PARENT_ID
            END
    )
    SELECT 'source categories' AS Metric, COUNT_BIG(*) AS Rows FROM CategorySource
    UNION ALL SELECT 'imported categories', COUNT_BIG(*) FROM dbo.ModernForumCategory
    UNION ALL SELECT 'source threads', COUNT_BIG(*) FROM ThreadSource
    UNION ALL SELECT 'imported threads', COUNT_BIG(*) FROM dbo.ModernForumThread
    UNION ALL SELECT 'source posts mapped to source threads', COUNT_BIG(*) FROM PostSource
    UNION ALL SELECT 'imported posts', COUNT_BIG(*) FROM dbo.ModernForumPost
    UNION ALL
    SELECT 'legacy rows not mapped to a source thread', COUNT_BIG(*)
    FROM dbo.Q_FORUM_TOPIC_T p
    WHERE NOT EXISTS (
        SELECT 1
        FROM ThreadSource threads
        WHERE threads.Q_FORUM_TOPIC_ID = CASE
            WHEN p.TOPIC_STARTER = 1 OR p.Q_FORUM_TOPIC_PARENT_ID = 0 THEN p.Q_FORUM_TOPIC_ID
            ELSE p.Q_FORUM_TOPIC_PARENT_ID
        END
    );

    SELECT Name, LastLegacyId, LastRowsImported, LastRunStartedAt, LastRunCompletedAt, UpdatedAt
    FROM dbo.ImportCheckpoint
    WHERE Name LIKE N'ModernForum.%'
    ORDER BY Name;
END;
GO

/*
Progress checks:

SELECT * FROM dbo.ImportCheckpoint WHERE Name LIKE N'ModernForum.%';

SELECT COUNT(*) AS Categories FROM dbo.ModernForumCategory;
SELECT COUNT(*) AS Threads FROM dbo.ModernForumThread;
SELECT COUNT(*) AS Posts FROM dbo.ModernForumPost;
EXEC dbo.ModernForum_GetImportReconciliation;

Rollback for proof-of-concept environments:

DROP PROCEDURE IF EXISTS dbo.ModernForum_GetImportReconciliation;
DROP PROCEDURE IF EXISTS dbo.ModernForum_ImportOrphanThreadsAndPosts;
DROP PROCEDURE IF EXISTS dbo.ModernForum_ImportPosts;
DROP PROCEDURE IF EXISTS dbo.ModernForum_ImportThreads;
DROP PROCEDURE IF EXISTS dbo.ModernForum_ImportCategories;
DROP PROCEDURE IF EXISTS dbo.ModernForum_ResetImport;
DROP PROCEDURE IF EXISTS dbo.ModernForum_EnsureSchema;
DROP TABLE IF EXISTS dbo.ModernForumPost;
DROP TABLE IF EXISTS dbo.ModernForumThread;
DROP TABLE IF EXISTS dbo.ModernForumCategory;
DELETE FROM dbo.ImportCheckpoint WHERE Name IN (N'ModernForum.Threads', N'ModernForum.Posts');
*/
