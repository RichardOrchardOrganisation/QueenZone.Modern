using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations;

/// <summary>Indexes the date component used by on-this-day lookups without scanning published history rows.</summary>
public partial class AddQueenHistoryMonthDayIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "EventMonthDay",
            table: "QueenHistoryEvents",
            type: "int",
            nullable: false,
            computedColumnSql: "MONTH([EventDate]) * 100 + DAY([EventDate])",
            stored: true);

        migrationBuilder.CreateIndex(
            name: "IX_QueenHistoryEvents_Published_MonthDay",
            table: "QueenHistoryEvents",
            columns: new[] { "IsPublished", "DatePrecision", "EventMonthDay" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_QueenHistoryEvents_Published_MonthDay",
            table: "QueenHistoryEvents");

        migrationBuilder.DropColumn(
            name: "EventMonthDay",
            table: "QueenHistoryEvents");
    }
}
