using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrosswordProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrosswordCompletions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrosswordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ElapsedSeconds = table.Column<int>(type: "int", nullable: false),
                    Clean = table.Column<bool>(type: "bit", nullable: false),
                    RankingEligible = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrosswordCompletions", x => x.Id);
                    table.CheckConstraint("CK_CrosswordCompletions_Time", "[ElapsedSeconds] >= 0");
                    table.ForeignKey(
                        name: "FK_CrosswordCompletions_Crosswords_CrosswordId",
                        column: x => x.CrosswordId,
                        principalTable: "Crosswords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CrosswordProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CrosswordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GridFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Letters = table.Column<string>(type: "nvarchar(225)", maxLength: 225, nullable: false),
                    RevealedCellsJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ElapsedSeconds = table.Column<int>(type: "int", nullable: false),
                    AutoCheckUsed = table.Column<bool>(type: "bit", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrosswordProgress", x => x.Id);
                    table.CheckConstraint("CK_CrosswordProgress_Time", "[ElapsedSeconds] >= 0");
                    table.ForeignKey(
                        name: "FK_CrosswordProgress_Crosswords_CrosswordId",
                        column: x => x.CrosswordId,
                        principalTable: "Crosswords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrosswordCompletions_CrosswordId_MemberId",
                table: "CrosswordCompletions",
                columns: new[] { "CrosswordId", "MemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrosswordCompletions_CrosswordId_RankingEligible_ElapsedSeconds",
                table: "CrosswordCompletions",
                columns: new[] { "CrosswordId", "RankingEligible", "ElapsedSeconds" });

            migrationBuilder.CreateIndex(
                name: "IX_CrosswordCompletions_MemberId_CompletedAt",
                table: "CrosswordCompletions",
                columns: new[] { "MemberId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CrosswordProgress_CrosswordId_MemberId",
                table: "CrosswordProgress",
                columns: new[] { "CrosswordId", "MemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrosswordProgress_MemberId",
                table: "CrosswordProgress",
                column: "MemberId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrosswordCompletions");

            migrationBuilder.DropTable(
                name: "CrosswordProgress");
        }
    }
}
