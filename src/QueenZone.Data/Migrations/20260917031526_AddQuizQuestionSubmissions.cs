using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQuizQuestionSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuizQuestionSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmitterMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuestionText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SourceNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewerEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AddedToQuizId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AddedToQuizAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizQuestionSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizQuestionSubmissions_MemberAccounts_SubmitterMemberId",
                        column: x => x.SubmitterMemberId,
                        principalTable: "MemberAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QuizQuestionSubmissionAuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QuizQuestionSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ActorEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizQuestionSubmissionAuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizQuestionSubmissionAuditLog_QuizQuestionSubmissions_QuizQuestionSubmissionId",
                        column: x => x.QuizQuestionSubmissionId,
                        principalTable: "QuizQuestionSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuizQuestionSubmissionOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QuizQuestionSubmissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuizQuestionSubmissionOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuizQuestionSubmissionOptions_QuizQuestionSubmissions_QuizQuestionSubmissionId",
                        column: x => x.QuizQuestionSubmissionId,
                        principalTable: "QuizQuestionSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestionSubmissionAuditLog_Submission_OccurredAt",
                table: "QuizQuestionSubmissionAuditLog",
                columns: new[] { "QuizQuestionSubmissionId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestionSubmissionOptions_Submission_DisplayOrder",
                table: "QuizQuestionSubmissionOptions",
                columns: new[] { "QuizQuestionSubmissionId", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestionSubmissions_Status_SubmittedAt",
                table: "QuizQuestionSubmissions",
                columns: new[] { "Status", "SubmittedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_QuizQuestionSubmissions_Submitter_SubmittedAt",
                table: "QuizQuestionSubmissions",
                columns: new[] { "SubmitterMemberId", "SubmittedAt" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuizQuestionSubmissionAuditLog");

            migrationBuilder.DropTable(
                name: "QuizQuestionSubmissionOptions");

            migrationBuilder.DropTable(
                name: "QuizQuestionSubmissions");
        }
    }
}
