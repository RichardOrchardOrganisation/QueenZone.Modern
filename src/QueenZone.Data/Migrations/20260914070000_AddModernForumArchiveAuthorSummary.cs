using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Materializes the archive-author identity and visible-post count so public author pages
/// do not repeatedly aggregate the million-row forum post table under crawler traffic.
/// </summary>
/// <remarks>
/// SQL source of truth: <c>docs/sql/011-modern-forum-archive-author-summary.sql</c>.
/// No EF model change; the table is an internal read projection maintained by a SQL trigger.
/// </remarks>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20260914070000_AddModernForumArchiveAuthorSummary")]
public partial class AddModernForumArchiveAuthorSummary : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
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
            """, suppressTransaction: true);

        migrationBuilder.Sql("""
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
            """, suppressTransaction: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS dbo.TR_ModernForumPost_RefreshArchiveAuthorSummary;
            DROP TABLE IF EXISTS dbo.ModernForumArchiveAuthorSummary;
            """, suppressTransaction: true);
    }
}
