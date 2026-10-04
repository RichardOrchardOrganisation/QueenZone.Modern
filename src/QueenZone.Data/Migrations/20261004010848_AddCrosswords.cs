using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrosswords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Crosswords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Difficulty = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Style = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    BlockMask = table.Column<string>(type: "nvarchar(225)", maxLength: 225, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PublishAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Crosswords", x => x.Id);
                    table.CheckConstraint("CK_Crosswords_Difficulty", "[Difficulty] IN ('easy', 'medium', 'hard')");
                    table.CheckConstraint("CK_Crosswords_Size", "[Width] BETWEEN 5 AND 15 AND [Height] BETWEEN 5 AND 15");
                    table.CheckConstraint("CK_Crosswords_Status", "[Status] IN ('Draft', 'Scheduled', 'Published', 'Archived')");
                    table.CheckConstraint("CK_Crosswords_Style", "[Style] IN ('american', 'british')");
                });

            migrationBuilder.CreateTable(
                name: "CrosswordEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrosswordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    Row = table.Column<int>(type: "int", nullable: false),
                    Column = table.Column<int>(type: "int", nullable: false),
                    Answer = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Clue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Enumeration = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrosswordEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrosswordEntries_Crosswords_CrosswordId",
                        column: x => x.CrosswordId,
                        principalTable: "Crosswords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrosswordEntries_CrosswordId_Number_Direction",
                table: "CrosswordEntries",
                columns: new[] { "CrosswordId", "Number", "Direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crosswords_Slug",
                table: "Crosswords",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Crosswords_Status_PublishAt",
                table: "Crosswords",
                columns: new[] { "Status", "PublishAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrosswordEntries");

            migrationBuilder.DropTable(
                name: "Crosswords");
        }
    }
}
