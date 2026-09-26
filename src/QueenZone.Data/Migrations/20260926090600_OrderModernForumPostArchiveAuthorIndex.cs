using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Completes the archive-author paging order in the author index. The prior index
/// stored PostedAt descending but its implicit clustered Id key ascending, so
/// ORDER BY PostedAt DESC, Id DESC needed a sort and thousands of LOB lookups.
/// </summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20260926090600_OrderModernForumPostArchiveAuthorIndex")]
public partial class OrderModernForumPostArchiveAuthorIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ModernForumPost', N'U')
                      AND name = N'IX_ModernForumPost_AuthorLegacyUserId_PostedAt')
                BEGIN
                    CREATE INDEX IX_ModernForumPost_AuthorLegacyUserId_PostedAt
                        ON dbo.ModernForumPost (AuthorLegacyUserId, PostedAt DESC, Id DESC)
                        INCLUDE (ThreadId, AuthorDisplayName, IsHidden)
                        WHERE AuthorLegacyUserId IS NOT NULL
                        WITH (DROP_EXISTING = ON);
                END
                ELSE
                BEGIN
                    CREATE INDEX IX_ModernForumPost_AuthorLegacyUserId_PostedAt
                        ON dbo.ModernForumPost (AuthorLegacyUserId, PostedAt DESC, Id DESC)
                        INCLUDE (ThreadId, AuthorDisplayName, IsHidden)
                        WHERE AuthorLegacyUserId IS NOT NULL;
                END
            END
            """, suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL
               AND EXISTS (
                   SELECT 1 FROM sys.indexes
                   WHERE object_id = OBJECT_ID(N'dbo.ModernForumPost', N'U')
                     AND name = N'IX_ModernForumPost_AuthorLegacyUserId_PostedAt')
            BEGIN
                CREATE INDEX IX_ModernForumPost_AuthorLegacyUserId_PostedAt
                    ON dbo.ModernForumPost (AuthorLegacyUserId, PostedAt DESC)
                    INCLUDE (ThreadId, AuthorDisplayName, IsHidden)
                    WHERE AuthorLegacyUserId IS NOT NULL
                    WITH (DROP_EXISTING = ON);
            END
            """, suppressTransaction: true);
    }
}
