using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace QueenZone.Data.Migrations;

/// <summary>Moves initial edit-concurrency statistics sampling out of the first web request.</summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20261022130000_PrecreateForumPostUpdatedAtStatistics")]
public partial class PrecreateForumPostUpdatedAtStatistics : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
        {
            return;
        }

        // Reuse any unfiltered leading-column histogram, including automatic/index statistics.
        // Default sampling avoids a forced full scan; MAXDOP bounds CPU, not the I/O cost.
        // Run outside the migration transaction so sampling holds no surrounding schema work open.
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'dbo.ModernForumPost', N'U') IS NOT NULL
                AND COL_LENGTH(N'dbo.ModernForumPost', N'UpdatedAt') IS NOT NULL
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM sys.stats AS s
                    INNER JOIN sys.stats_columns AS sc
                        ON sc.object_id = s.object_id AND sc.stats_id = s.stats_id
                    WHERE s.object_id = OBJECT_ID(N'dbo.ModernForumPost', N'U')
                        AND sc.stats_column_id = 1
                        AND sc.column_id = COLUMNPROPERTY(s.object_id, N'UpdatedAt', 'ColumnId')
                        AND s.has_filter = 0
                        AND s.is_temporary = 0
                )
            BEGIN
                EXEC(N'CREATE STATISTICS [ST_ModernForumPost_UpdatedAt]
                    ON [dbo].[ModernForumPost] ([UpdatedAt]) WITH MAXDOP = 1;');
            END;
            """, suppressTransaction: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally retain optimizer metadata: Up may have reused independently owned stats,
        // and removing newly created stats would put initial sampling back on the next edit.
    }
}
