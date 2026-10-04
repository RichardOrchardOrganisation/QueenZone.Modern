using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrosswordPlayVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlayVersion",
                table: "Crosswords",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()");

            migrationBuilder.AddColumn<Guid>(
                name: "PlayVersion",
                table: "CrosswordProgress",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("UPDATE progress SET PlayVersion = puzzle.PlayVersion FROM CrosswordProgress progress INNER JOIN Crosswords puzzle ON progress.CrosswordId = puzzle.Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PlayVersion",
                table: "Crosswords");

            migrationBuilder.DropColumn(
                name: "PlayVersion",
                table: "CrosswordProgress");
        }
    }
}
