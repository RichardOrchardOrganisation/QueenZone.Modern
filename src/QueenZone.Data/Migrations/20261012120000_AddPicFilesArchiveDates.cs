using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>
/// Adds the event date and photograph date to <c>PIC_FILES_T</c>, kept apart from the existing
/// <c>Date_time</c> (the upload/import timestamp, i.e. the added-to-archive date) and
/// <c>PICTURE_YEAR</c>. Each date is a <c>date</c> plus a one-letter precision (<c>Y</c>, <c>M</c>,
/// <c>D</c>) because archive photos are often datable only to a year or month. All columns are
/// nullable and nothing is backfilled: unknown stays unknown. The legacy table is altered under
/// ADR 0021; the migration is a no-op where the table does not exist.
/// </summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20261012120000_AddPicFilesArchiveDates")]
public partial class AddPicFilesArchiveDates : Migration
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

            IF COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_EVENT_DATE') IS NULL
                ALTER TABLE dbo.PIC_FILES_T ADD PIC_EVENT_DATE date NULL;
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PIC_FILES_T', N'U') IS NULL
            BEGIN
                RETURN;
            END;

            IF COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_EVENT_PRECISION') IS NULL
                ALTER TABLE dbo.PIC_FILES_T ADD PIC_EVENT_PRECISION char(1) NULL;
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PIC_FILES_T', N'U') IS NULL
            BEGIN
                RETURN;
            END;

            IF COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_TAKEN_DATE') IS NULL
                ALTER TABLE dbo.PIC_FILES_T ADD PIC_TAKEN_DATE date NULL;
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PIC_FILES_T', N'U') IS NULL
            BEGIN
                RETURN;
            END;

            IF COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_TAKEN_PRECISION') IS NULL
                ALTER TABLE dbo.PIC_FILES_T ADD PIC_TAKEN_PRECISION char(1) NULL;
            """);
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

            IF COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_EVENT_PRECISION') IS NOT NULL
                ALTER TABLE dbo.PIC_FILES_T DROP COLUMN PIC_EVENT_PRECISION;
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PIC_FILES_T', N'U') IS NULL
            BEGIN
                RETURN;
            END;

            IF COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_EVENT_DATE') IS NOT NULL
                ALTER TABLE dbo.PIC_FILES_T DROP COLUMN PIC_EVENT_DATE;
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PIC_FILES_T', N'U') IS NULL
            BEGIN
                RETURN;
            END;

            IF COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_TAKEN_PRECISION') IS NOT NULL
                ALTER TABLE dbo.PIC_FILES_T DROP COLUMN PIC_TAKEN_PRECISION;
            """);

        migrationBuilder.Sql(
            """
            IF OBJECT_ID(N'dbo.PIC_FILES_T', N'U') IS NULL
            BEGIN
                RETURN;
            END;

            IF COL_LENGTH(N'dbo.PIC_FILES_T', N'PIC_TAKEN_DATE') IS NOT NULL
                ALTER TABLE dbo.PIC_FILES_T DROP COLUMN PIC_TAKEN_DATE;
            """);
    }
}
