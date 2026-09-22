using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Persists the longest original side of <c>PIC_FILES_T</c> so size filters compare a bare
/// column. <c>PIC_WIDTH</c> and <c>PIC_HEIGHT</c> are <c>smallint</c>, which is why list
/// projections still cast them to <c>int</c> for EF materialization. The filter predicate
/// must not wrap those columns in <c>CAST</c>.
/// </summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20260922053000_AddPicFilesLongestSide")]
public partial class AddPicFilesLongestSide : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PIC_FILES_T', N'U') IS NULL
            BEGIN
                RETURN;
            END;

            IF COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_LONGEST_SIDE') IS NULL
            BEGIN
                ALTER TABLE dbo.PIC_FILES_T ADD PIC_LONGEST_SIDE AS (
                    CAST(
                        CASE
                            WHEN ISNULL(PIC_WIDTH, 0) > 0 AND ISNULL(PIC_HEIGHT, 0) > 0
                                THEN CASE WHEN PIC_WIDTH > PIC_HEIGHT THEN PIC_WIDTH ELSE PIC_HEIGHT END
                            ELSE 0
                        END
                    AS int)
                ) PERSISTED;
            END;
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PIC_FILES_T', N'U') IS NULL
               OR COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_LONGEST_SIDE') IS NULL
            BEGIN
                RETURN;
            END;

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.PIC_FILES_T', N'U')
                  AND name = N'IX_PIC_FILES_T_Cat_Display_LongestSide')
            BEGIN
                CREATE NONCLUSTERED INDEX IX_PIC_FILES_T_Cat_Display_LongestSide
                ON dbo.PIC_FILES_T
                (
                    Cat_ID ASC,
                    DISPLAY ASC,
                    PIC_LONGEST_SIDE ASC
                )
                INCLUDE
                (
                    Date_time,
                    PIC_ID,
                    PIC_WIDTH,
                    PIC_HEIGHT
                )
                WITH (SORT_IN_TEMPDB = ON);
            END;
            """,
            suppressTransaction: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PIC_FILES_T', N'U') IS NULL
            BEGIN
                RETURN;
            END;

            DROP INDEX IF EXISTS IX_PIC_FILES_T_Cat_Display_LongestSide ON dbo.PIC_FILES_T;

            IF COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_LONGEST_SIDE') IS NOT NULL
            BEGIN
                ALTER TABLE dbo.PIC_FILES_T DROP COLUMN PIC_LONGEST_SIDE;
            END;
            """);
    }
}
