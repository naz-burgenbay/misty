using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                schema: "comm",
                table: "UserBlock",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                schema: "comm",
                table: "ModerationAction",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                schema: "msg",
                table: "Message",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                schema: "comm",
                table: "Membership",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                schema: "comm",
                table: "Friendship",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                schema: "comm",
                table: "FriendRequest",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                schema: "comm",
                table: "Conversation",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                schema: "comm",
                table: "ChannelRole",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                schema: "comm",
                table: "ChannelInvite",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                schema: "comm",
                table: "UserBlock");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "comm",
                table: "ModerationAction");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "msg",
                table: "Message");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "comm",
                table: "Membership");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "comm",
                table: "Friendship");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "comm",
                table: "FriendRequest");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "comm",
                table: "Conversation");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "comm",
                table: "ChannelRole");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "comm",
                table: "ChannelInvite");
        }
    }
}
