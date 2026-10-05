using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QueenZone.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberThemePreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "ThemePreference",
                table: "MemberAccounts",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThemePreference",
                table: "MemberAccounts");
        }
    }
}
