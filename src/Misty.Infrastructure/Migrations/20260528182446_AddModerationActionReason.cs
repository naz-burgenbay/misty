using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationActionReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "comm",
                table: "ModerationAction",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "comm",
                table: "ModerationAction");
        }
    }
}
