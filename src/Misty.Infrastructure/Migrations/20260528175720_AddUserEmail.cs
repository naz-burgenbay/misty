using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    public partial class AddUserEmail : Migration
    {
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

            migrationBuilder.Sql(
                "UPDATE [users].[User] SET [Email] = CONCAT(N'legacy+', LOWER([Username]), N'@local.invalid') WHERE [Email] = N'';");

            migrationBuilder.CreateIndex(
                name: "UX_User_Email",
                schema: "users",
                table: "User",
                column: "Email",
                unique: true);
        }

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
