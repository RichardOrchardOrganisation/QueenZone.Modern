using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Keeps body/edit-metadata updates independent of archive-author summary refreshes.
/// </summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20261022023000_SkipArchiveAuthorSummaryForPostEdits")]
public partial class SkipArchiveAuthorSummaryForPostEdits : Migration
{
    private const string UpdateGuardSql = """
                -- Body edits do not change the archive-author projection. Avoid its reads
                -- and summary-row locks unless an input to that projection is being updated.
                IF EXISTS (SELECT 1 FROM inserted)
                   AND EXISTS (SELECT 1 FROM deleted)
                   AND NOT (UPDATE(AuthorLegacyUserId) OR UPDATE(AuthorDisplayName)
                            OR UPDATE(AuthorJoinedAt) OR UPDATE(IsHidden)
                            OR UPDATE(PostedAt) OR UPDATE(Id))
                    RETURN;
        """;

    private const string TriggerSql = """
            CREATE OR ALTER TRIGGER dbo.TR_ModernForumPost_RefreshArchiveAuthorSummary
            ON dbo.ModernForumPost
            AFTER INSERT, UPDATE, DELETE
            AS
            BEGIN
                SET NOCOUNT ON;

                -- Body edits do not change the archive-author projection. Avoid its reads
                -- and summary-row locks unless an input to that projection is being updated.
                IF EXISTS (SELECT 1 FROM inserted)
                   AND EXISTS (SELECT 1 FROM deleted)
                   AND NOT (UPDATE(AuthorLegacyUserId) OR UPDATE(AuthorDisplayName)
                            OR UPDATE(AuthorJoinedAt) OR UPDATE(IsHidden)
                            OR UPDATE(PostedAt) OR UPDATE(Id))
                    RETURN;

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
        """;

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(TriggerSql, suppressTransaction: true);

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(TriggerSql.Replace(UpdateGuardSql, string.Empty, StringComparison.Ordinal),
            suppressTransaction: true);
}
