using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>Adds duration to the legacy stage table. Blob backfill runs separately via QueenZone.Tools.</summary>
[DbContext(typeof(QueenZoneDbContext))]
[Migration("20260923090000_AddFanPerformanceDuration")]
public partial class AddFanPerformanceDuration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE dbo.Q_STAGE_T ADD DurationSeconds int NULL;");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("ALTER TABLE dbo.Q_STAGE_T DROP COLUMN DurationSeconds;");
}
