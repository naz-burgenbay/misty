using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UnifyDeletionFieldShape : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                schema: "comm",
                table: "ModerationAction",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                schema: "msg",
                table: "Message",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                schema: "comm",
                table: "FriendRequest",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                schema: "comm",
                table: "FriendRequest",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                schema: "comm",
                table: "Conversation",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                schema: "comm",
                table: "Conversation",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                schema: "comm",
                table: "ChannelRole",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                schema: "comm",
                table: "ChannelRole",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                schema: "comm",
                table: "ChannelInvite",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                schema: "comm",
                table: "ChannelInvite",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "comm",
                table: "ModerationAction");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "msg",
                table: "Message");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "comm",
                table: "FriendRequest");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                schema: "comm",
                table: "FriendRequest");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "comm",
                table: "Conversation");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                schema: "comm",
                table: "Conversation");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "comm",
                table: "ChannelRole");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                schema: "comm",
                table: "ChannelRole");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "comm",
                table: "ChannelInvite");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                schema: "comm",
                table: "ChannelInvite");
        }
    }
}
