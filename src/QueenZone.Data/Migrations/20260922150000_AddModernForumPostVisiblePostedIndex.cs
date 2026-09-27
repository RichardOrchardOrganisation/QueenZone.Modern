using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Adds the filtered chronological index used by the cached count of visible forum replies
/// posted since midnight UTC. The imported forum table is excluded from generated migrations,
/// so the SQL Server index is managed explicitly here.
/// </summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20260922150000_AddModernForumPostVisiblePostedIndex")]
public partial class AddModernForumPostVisiblePostedIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL
               AND COL_LENGTH(N'dbo.ModernForumPost', N'PostedAt') IS NOT NULL
               AND COL_LENGTH(N'dbo.ModernForumPost', N'IsHidden') IS NOT NULL
               AND NOT EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ModernForumPost', N'U')
                      AND name = N'IX_ModernForumPost_PostedAt_Visible')
            BEGIN
                CREATE INDEX IX_ModernForumPost_PostedAt_Visible
                    ON dbo.ModernForumPost (PostedAt)
                    WHERE IsHidden = 0;
            END
            """, suppressTransaction: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL
               AND EXISTS (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.ModernForumPost', N'U')
                      AND name = N'IX_ModernForumPost_PostedAt_Visible')
            BEGIN
                DROP INDEX IX_ModernForumPost_PostedAt_Visible
                    ON dbo.ModernForumPost;
            END
            """, suppressTransaction: true);
    }
}
