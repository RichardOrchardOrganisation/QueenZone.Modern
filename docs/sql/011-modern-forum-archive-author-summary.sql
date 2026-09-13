-- Materialized archive-author summary used by EfForumArchiveAuthorRepository.
-- The matching EF migration is 20260914070000_AddModernForumArchiveAuthorSummary.

IF OBJECT_ID(N'dbo.ModernForumArchiveAuthorSummary', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ModernForumArchiveAuthorSummary
    (
        LegacyUserId int NOT NULL,
        DisplayName nvarchar(100) NOT NULL,
        MemberSince datetime2(0) NULL,
        PostCount int NOT NULL,
        RefreshedAt datetime2(0) NOT NULL
            CONSTRAINT DF_ModernForumArchiveAuthorSummary_RefreshedAt
            DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_ModernForumArchiveAuthorSummary
            PRIMARY KEY CLUSTERED (LegacyUserId)
    );
END;

IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL
BEGIN
    DELETE FROM dbo.ModernForumArchiveAuthorSummary;

    ;WITH VisibleCounts AS
    (
        SELECT
            AuthorLegacyUserId AS LegacyUserId,
            COUNT_BIG(*) AS PostCount
        FROM dbo.ModernForumPost
        WHERE AuthorLegacyUserId IS NOT NULL
          AND IsHidden = 0
        GROUP BY AuthorLegacyUserId
    )
    INSERT dbo.ModernForumArchiveAuthorSummary
        (LegacyUserId, DisplayName, MemberSince, PostCount, RefreshedAt)
    SELECT
        counts.LegacyUserId,
        latest.AuthorDisplayName,
        latest.AuthorJoinedAt,
        CONVERT(int, counts.PostCount),
        SYSUTCDATETIME()
    FROM VisibleCounts AS counts
    CROSS APPLY
    (
        SELECT TOP (1)
            post.AuthorDisplayName,
            post.AuthorJoinedAt
        FROM dbo.ModernForumPost AS post
        WHERE post.AuthorLegacyUserId = counts.LegacyUserId
          AND post.IsHidden = 0
        ORDER BY post.PostedAt DESC, post.Id DESC
    ) AS latest;
END;
GO

CREATE OR ALTER TRIGGER dbo.TR_ModernForumPost_RefreshArchiveAuthorSummary
ON dbo.ModernForumPost
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Affected TABLE
    (
        LegacyUserId int NOT NULL PRIMARY KEY
    );

    INSERT @Affected (LegacyUserId)
    SELECT AuthorLegacyUserId
    FROM inserted
    WHERE AuthorLegacyUserId IS NOT NULL
    UNION
    SELECT AuthorLegacyUserId
    FROM deleted
    WHERE AuthorLegacyUserId IS NOT NULL;

    DELETE summary
    FROM dbo.ModernForumArchiveAuthorSummary AS summary
    INNER JOIN @Affected AS affected
        ON affected.LegacyUserId = summary.LegacyUserId;

    INSERT dbo.ModernForumArchiveAuthorSummary
        (LegacyUserId, DisplayName, MemberSince, PostCount, RefreshedAt)
    SELECT
        affected.LegacyUserId,
        latest.AuthorDisplayName,
        latest.AuthorJoinedAt,
        CONVERT(int, counts.PostCount),
        SYSUTCDATETIME()
    FROM @Affected AS affected
    CROSS APPLY
    (
        SELECT COUNT_BIG(*) AS PostCount
        FROM dbo.ModernForumPost AS post
        WHERE post.AuthorLegacyUserId = affected.LegacyUserId
          AND post.IsHidden = 0
    ) AS counts
    CROSS APPLY
    (
        SELECT TOP (1)
            post.AuthorDisplayName,
            post.AuthorJoinedAt
        FROM dbo.ModernForumPost AS post
        WHERE post.AuthorLegacyUserId = affected.LegacyUserId
          AND post.IsHidden = 0
        ORDER BY post.PostedAt DESC, post.Id DESC
    ) AS latest
    WHERE counts.PostCount > 0;
END;
GO
