using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Lists only resolvable forum topics and counts them live after visibility changes.
/// No EF model changes are required for these procedure replacements.
/// </summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20261022120000_AlignForumSitemapVisibility")]
public partial class AlignForumSitemapVisibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.ModernForum_GetTopicSitemapCount AS
            BEGIN
                SET NOCOUNT ON;
                SELECT CONVERT(int, COUNT_BIG(*))
                FROM dbo.ModernForumThread t
                INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
                WHERE t.IsHidden = 0
                  AND NULLIF(LTRIM(RTRIM(t.Title)), '') IS NOT NULL
                  AND t.LegacyForumId > 0
                  AND c.LegacyForumId > 0;
            END;
            """, suppressTransaction: true);

        migrationBuilder.Sql("""
            CREATE OR ALTER PROCEDURE dbo.ModernForum_GetTopicSitemapPage @Offset int, @PageSize int
            AS
            BEGIN
                SET NOCOUNT ON;
                SELECT t.LegacyTopicId AS TopicId, LTRIM(RTRIM(t.Title)) AS Title, t.LastActivityAt
                FROM dbo.ModernForumThread t WITH (INDEX(IX_ModernForumThread_Sitemap))
                INNER JOIN dbo.ModernForumCategory c ON c.Id = t.CategoryId
                WHERE t.IsHidden = 0
                  AND NULLIF(LTRIM(RTRIM(t.Title)), '') IS NOT NULL
                  AND t.LegacyForumId > 0
                  AND c.LegacyForumId > 0
                ORDER BY t.LegacyTopicId ASC OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            END;
            """, suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Sitemap visibility is a safety control and cannot be removed automatically.");
    }
}
