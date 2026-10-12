using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoCorrectionProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoCorrectionProposals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PicId = table.Column<int>(type: "int", nullable: false),
                    SubmitterMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CurrentValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ProposedValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    EvidenceUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EvidenceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Confidence = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PublicCreditName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MessageToMember = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AppliedFromValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoCorrectionProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoCorrectionProposals_MemberAccounts_SubmitterMemberId",
                        column: x => x.SubmitterMemberId,
                        principalTable: "MemberAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhotoCorrectionAuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PhotoCorrectionProposalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActorEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoCorrectionAuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoCorrectionAuditLog_PhotoCorrectionProposals_PhotoCorrectionProposalId",
                        column: x => x.PhotoCorrectionProposalId,
                        principalTable: "PhotoCorrectionProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoCorrectionAuditLog_Proposal_OccurredAt",
                table: "PhotoCorrectionAuditLog",
                columns: new[] { "PhotoCorrectionProposalId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoCorrectionProposals_Photo_SubmittedAt",
                table: "PhotoCorrectionProposals",
                columns: new[] { "PicId", "SubmittedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoCorrectionProposals_Status_SubmittedAt",
                table: "PhotoCorrectionProposals",
                columns: new[] { "Status", "SubmittedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoCorrectionProposals_Submitter_SubmittedAt",
                table: "PhotoCorrectionProposals",
                columns: new[] { "SubmitterMemberId", "SubmittedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoCorrectionAuditLog");

            migrationBuilder.DropTable(
                name: "PhotoCorrectionProposals");
        }
    }
}
