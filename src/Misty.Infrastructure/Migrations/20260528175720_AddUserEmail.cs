using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                schema: "users",
                table: "User",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "UX_User_Email",
                schema: "users",
                table: "User",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_User_Email",
                schema: "users",
                table: "User");

            migrationBuilder.DropColumn(
                name: "Email",
                schema: "users",
                table: "User");
        }
    }
}
