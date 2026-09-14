using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddForumPostReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ForumPostReportAuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActorEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumPostReportAuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ForumPostReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostId = table.Column<int>(type: "int", nullable: false),
                    TopicId = table.Column<int>(type: "int", nullable: false),
                    ReporterMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReportedMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PostBodySnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AuthorDisplayNameSnapshot = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PostCreatedAtSnapshot = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ThreadTitleSnapshot = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContextJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForumPostReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForumPostReports_MemberAccounts_ReportedMemberId",
                        column: x => x.ReportedMemberId,
                        principalTable: "MemberAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ForumPostReports_MemberAccounts_ReporterMemberId",
                        column: x => x.ReporterMemberId,
                        principalTable: "MemberAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForumPostReportAuditLog_ReportId_OccurredAt",
                table: "ForumPostReportAuditLog",
                columns: new[] { "ReportId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ForumPostReports_ReportedMember",
                table: "ForumPostReports",
                column: "ReportedMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_ForumPostReports_Reporter_Post",
                table: "ForumPostReports",
                columns: new[] { "ReporterMemberId", "PostId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForumPostReports_Status_CreatedAt",
                table: "ForumPostReports",
                columns: new[] { "Status", "CreatedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForumPostReportAuditLog");

            migrationBuilder.DropTable(
                name: "ForumPostReports");
        }
    }
}
