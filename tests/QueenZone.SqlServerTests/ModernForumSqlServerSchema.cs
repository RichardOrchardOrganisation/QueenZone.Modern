using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using QueenZone.Data.Migrations;

namespace QueenZone.SqlServerTests;

/// <summary>
/// Scratch-database DDL for modern forum procedures and read-stat tables (#1892).
/// Base tables are <c>ExcludeFromMigrations</c>; this installs the current production
/// shape from <c>docs/sql/004</c> / <c>006</c> plus later visibility, nvarchar(max),
/// sequence, and poll columns — not the whole import script or migration chain.
/// </summary>
public static class ModernForumSqlServerSchema
{
    public const string TitleFreeTextSource = "FREETEXTTABLE(dbo.ModernForumThread, Title, @Query) ft";
    public const string BodyFreeTextSource = "FREETEXTTABLE(dbo.ModernForumPost, BodyHtml, @Query) ft";

    private const string TitleLikeSource = """
        (
            SELECT Id AS [KEY], 2 AS [RANK]
            FROM dbo.ModernForumThread
            WHERE Title LIKE N'%' + @Query + N'%'
        ) ft
        """;

    private const string BodyLikeSource = """
        (
            SELECT Id AS [KEY], 1 AS [RANK]
            FROM dbo.ModernForumPost
            WHERE BodyHtml LIKE N'%' + @Query + N'%'
        ) ft
        """;

    public static readonly string[] TableAndIndexBatches =
    [
        """
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
        """,
        """
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
            IsHidden bit NOT NULL CONSTRAINT DF_ModernForumThread_IsHidden DEFAULT (0),
            StarterAttachment varchar(120) NULL,
            StarterFileSize varchar(12) NULL,
            StarterAttachCount int NOT NULL,
            ImportedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumThread_ImportedAt DEFAULT (sysutcdatetime()),
            UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumThread_UpdatedAt DEFAULT (sysutcdatetime()),
            CONSTRAINT UQ_ModernForumThread_LegacyTopicId UNIQUE (LegacyTopicId),
            CONSTRAINT FK_ModernForumThread_Category FOREIGN KEY (CategoryId)
                REFERENCES dbo.ModernForumCategory (Id)
        );
        """,
        """
        CREATE TABLE dbo.ModernForumPost
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModernForumPost PRIMARY KEY,
            LegacyPostId int NOT NULL,
            LegacyThreadTopicId int NOT NULL,
            ThreadId bigint NOT NULL,
            LegacyForumId int NOT NULL,
            AuthorLegacyUserId int NULL,
            AuthorMemberId uniqueidentifier NULL,
            AuthorDisplayName nvarchar(100) NOT NULL,
            AuthorPostCount int NULL,
            AuthorJoinedAt datetime2(0) NULL,
            BodyHtml nvarchar(max) NOT NULL,
            SignatureHtml nvarchar(max) NULL,
            PostedAt datetime2(0) NULL,
            EditedAt datetime2 NULL,
            EditCount int NOT NULL CONSTRAINT DF_ModernForumPost_EditCount DEFAULT (0),
            LegacyDiscography tinyint NOT NULL,
            AuthorUserValidated bit NULL,
            Attachment varchar(120) NULL,
            FileSize varchar(12) NULL,
            AttachCount int NOT NULL,
            IsHidden bit NOT NULL CONSTRAINT DF_ModernForumPost_IsHidden DEFAULT (0),
            ImportedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumPost_ImportedAt DEFAULT (sysutcdatetime()),
            UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumPost_UpdatedAt DEFAULT (sysutcdatetime()),
            CONSTRAINT UQ_ModernForumPost_LegacyPostId UNIQUE (LegacyPostId),
            CONSTRAINT FK_ModernForumPost_Thread FOREIGN KEY (ThreadId)
                REFERENCES dbo.ModernForumThread (Id)
        );
        """,
        """
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
        """,
        """
        CREATE TABLE dbo.ModernForumThreadReadStats
        (
            ThreadId bigint NOT NULL CONSTRAINT PK_ModernForumThreadReadStats PRIMARY KEY,
            LegacyTopicId int NOT NULL CONSTRAINT UQ_ModernForumThreadReadStats_LegacyTopicId UNIQUE,
            PostCount int NOT NULL,
            UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumThreadReadStats_UpdatedAt DEFAULT (sysutcdatetime()),
            CONSTRAINT FK_ModernForumThreadReadStats_Thread FOREIGN KEY (ThreadId)
                REFERENCES dbo.ModernForumThread (Id)
        );
        """,
        """
        CREATE TABLE dbo.ModernForumArchiveReadStats
        (
            Id tinyint NOT NULL CONSTRAINT PK_ModernForumArchiveReadStats PRIMARY KEY,
            TotalThreads int NOT NULL,
            SitemapTopicCount int NOT NULL,
            UpdatedAt datetime2(0) NOT NULL CONSTRAINT DF_ModernForumArchiveReadStats_UpdatedAt DEFAULT (sysutcdatetime()),
            CONSTRAINT CK_ModernForumArchiveReadStats_SingleRow CHECK (Id = 1)
        );
        """,
        """
        CREATE TABLE dbo.ForumPostAttachments
        (
            Id uniqueidentifier NOT NULL CONSTRAINT PK_ForumPostAttachments PRIMARY KEY,
            PostId bigint NOT NULL,
            LegacyPostId int NOT NULL,
            OriginalFileName nvarchar(255) NOT NULL,
            BlobPath nvarchar(512) NOT NULL,
            ContainerName nvarchar(64) NOT NULL,
            FileSizeBytes bigint NOT NULL,
            MimeType nvarchar(100) NOT NULL,
            UploadedAt datetimeoffset NOT NULL,
            DownloadCount int NOT NULL CONSTRAINT DF_ForumPostAttachments_DownloadCount DEFAULT (0)
        );
        CREATE INDEX IX_ForumPostAttachments_LegacyPostId ON dbo.ForumPostAttachments (LegacyPostId);
        CREATE INDEX IX_ForumPostAttachments_PostId ON dbo.ForumPostAttachments (PostId);
        """,
        """
        CREATE TABLE dbo.ForumPolls
        (
            Id uniqueidentifier NOT NULL CONSTRAINT PK_ForumPolls PRIMARY KEY,
            ThreadId bigint NOT NULL,
            LegacyTopicId int NOT NULL,
            Question nvarchar(300) NOT NULL,
            IsMultiChoice bit NOT NULL,
            MaxChoices int NULL,
            ClosesAt datetimeoffset NULL,
            ClosedAt datetimeoffset NULL,
            CreatedByMemberId uniqueidentifier NOT NULL,
            CreatedAt datetimeoffset NOT NULL
        );
        CREATE UNIQUE INDEX UQ_ForumPolls_LegacyTopicId ON dbo.ForumPolls (LegacyTopicId);
        CREATE UNIQUE INDEX UQ_ForumPolls_ThreadId ON dbo.ForumPolls (ThreadId);
        """,
        """
        CREATE INDEX IX_ModernForumThread_CategoryStarter_Latest
        ON dbo.ModernForumThread (CategoryId, IsLegacyTopicStarter, LastActivityAt DESC, LegacyTopicId DESC)
        INCLUDE (Title);
        """,
        """
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
        """,
        """
        CREATE INDEX IX_ModernForumThread_Sitemap
        ON dbo.ModernForumThread (LegacyTopicId ASC)
        INCLUDE (Title, LastActivityAt);
        """,
        """
        CREATE INDEX IX_ModernForumPost_Thread_Posted
        ON dbo.ModernForumPost (ThreadId, LegacyPostId ASC)
        INCLUDE (PostedAt, AuthorDisplayName);
        """,
    ];

    /// <summary>
    /// Current <c>ModernForum_GetCategories</c> from <c>docs/sql/006-modern-forum-read-path.sql</c>.
    /// HideSuspendedMemberTopics does not replace this procedure.
    /// </summary>
    public const string GetCategoriesProcedureSql = """
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
        """;

    public static string GetProcedureSql(string name) =>
        new HideSuspendedMemberTopics().UpOperations
            .OfType<SqlOperation>()
            .Single(operation => operation.Sql.Contains(
                $"CREATE OR ALTER PROCEDURE dbo.{name}",
                StringComparison.Ordinal))
            .Sql;

    public static string GetSequenceSql() =>
        new ForumCreatePerfSequencesAndHasPoll().UpOperations
            .OfType<SqlOperation>()
            .Single(operation => operation.Sql.Contains("ForumLegacyTopicIdSeq", StringComparison.Ordinal))
            .Sql;

    public static string SearchProcedureSql() => GetProcedureSql("ModernForum_SearchThreads");

    public static string SearchProcedureSqlForLocalDb()
    {
        var sql = SearchProcedureSql();
        var titleCount = sql.Split(TitleFreeTextSource, StringSplitOptions.None).Length - 1;
        var bodyCount = sql.Split(BodyFreeTextSource, StringSplitOptions.None).Length - 1;
        if (titleCount != 2 || bodyCount != 2)
        {
            throw new InvalidOperationException(
                $"Expected two title and two body FREETEXTTABLE sources, found {titleCount} / {bodyCount}.");
        }

        return sql
            .Replace(TitleFreeTextSource, TitleLikeSource, StringComparison.Ordinal)
            .Replace(BodyFreeTextSource, BodyLikeSource, StringComparison.Ordinal);
    }

    public static async Task InstallAsync(DbContext schema, CancellationToken cancellationToken = default)
    {
        foreach (var batch in TableAndIndexBatches)
        {
            await schema.Database.ExecuteSqlRawAsync(batch, cancellationToken);
        }

        await schema.Database.ExecuteSqlRawAsync(GetCategoriesProcedureSql, cancellationToken);
        foreach (var name in new[]
        {
            "ModernForum_GetCategoryByLegacyForumId",
            "ModernForum_GetCategoryThreadsPage",
            "ModernForum_GetTopicPostsPage",
            "ModernForum_GetTotalThreadCount",
            "ModernForum_GetTopicSitemapCount",
            "ModernForum_GetTopicSitemapPage",
            "ModernForum_RefreshReadStats",
        })
        {
            await schema.Database.ExecuteSqlRawAsync(GetProcedureSql(name), cancellationToken);
        }

        await schema.Database.ExecuteSqlRawAsync(SearchProcedureSqlForLocalDb(), cancellationToken);
    }

    public static Task InstallSequencesAsync(DbContext schema, CancellationToken cancellationToken = default) =>
        schema.Database.ExecuteSqlRawAsync(GetSequenceSql(), cancellationToken);
}
