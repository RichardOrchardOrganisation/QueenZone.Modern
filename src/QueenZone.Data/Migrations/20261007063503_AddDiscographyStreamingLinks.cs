using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscographyStreamingLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiscographyStreamingLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AlbumId = table.Column<int>(type: "int", nullable: false),
                    AlbumSongId = table.Column<int>(type: "int", nullable: true),
                    Provider = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ExternalId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Url = table.Column<string>(type: "varchar(300)", unicode: false, maxLength: 300, nullable: false),
                    Source = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscographyStreamingLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscographyStreamingLinks_Album_Provider",
                table: "DiscographyStreamingLinks",
                columns: new[] { "AlbumId", "Provider" },
                unique: true,
                filter: "[AlbumSongId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DiscographyStreamingLinks_AlbumId",
                table: "DiscographyStreamingLinks",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscographyStreamingLinks_Track_Provider",
                table: "DiscographyStreamingLinks",
                columns: new[] { "AlbumSongId", "Provider" },
                unique: true,
                filter: "[AlbumSongId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscographyStreamingLinks");
        }
    }
}
