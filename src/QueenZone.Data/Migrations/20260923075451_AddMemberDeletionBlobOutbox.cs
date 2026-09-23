using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberDeletionBlobOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemberDeletionBlobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MemberAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Container = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BlobPath = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberDeletionBlobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemberDeletionBlobs_CreatedAt",
                table: "MemberDeletionBlobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MemberDeletionBlobs_MemberAccountId",
                table: "MemberDeletionBlobs",
                column: "MemberAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemberDeletionBlobs");
        }
    }
}
