using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MessageReactionEmojiBinaryCollation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EmojiCode is part of the composite primary key, so SQL Server refuses ALTER COLUMN
            // while the constraint references it. Drop the PK, alter the collation, then recreate.
            migrationBuilder.DropPrimaryKey(
                name: "PK_MessageReaction",
                schema: "msg",
                table: "MessageReaction");

            migrationBuilder.AlterColumn<string>(
                name: "EmojiCode",
                schema: "msg",
                table: "MessageReaction",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AddPrimaryKey(
                name: "PK_MessageReaction",
                schema: "msg",
                table: "MessageReaction",
                columns: new[] { "MessageId", "UserId", "EmojiCode" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_MessageReaction",
                schema: "msg",
                table: "MessageReaction");

            migrationBuilder.AlterColumn<string>(
                name: "EmojiCode",
                schema: "msg",
                table: "MessageReaction",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldCollation: "Latin1_General_100_BIN2");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MessageReaction",
                schema: "msg",
                table: "MessageReaction",
                columns: new[] { "MessageId", "UserId", "EmojiCode" });
        }
    }
}
