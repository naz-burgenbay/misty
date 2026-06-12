using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailConfirmed",
                schema: "users",
                table: "User",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EmailConfirmationToken",
                schema: "users",
                table: "User",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            // Existing users and bot should be confirmed so they can still log in
            migrationBuilder.Sql("UPDATE [users].[User] SET [EmailConfirmed] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailConfirmed",
                schema: "users",
                table: "User");

            migrationBuilder.DropColumn(
                name: "EmailConfirmationToken",
                schema: "users",
                table: "User");
        }
    }
}
