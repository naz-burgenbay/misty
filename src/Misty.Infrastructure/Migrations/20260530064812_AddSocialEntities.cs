using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    public partial class AddSocialEntities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "comm",
                table: "Channel",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IconUrl",
                schema: "comm",
                table: "Channel",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChannelInvite",
                schema: "comm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvitedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelInvite", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelInvite_Channel_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "comm",
                        principalTable: "Channel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FriendRequest",
                schema: "comm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiverId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FriendRequest", x => x.Id);
                    table.CheckConstraint("CK_FriendRequest_SenderNeReceiver", "[SenderId] <> [ReceiverId]");
                });

            migrationBuilder.CreateTable(
                name: "Friendship",
                schema: "comm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserAId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserBId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friendship", x => x.Id);
                    table.CheckConstraint("CK_Friendship_UserAltUserB", "[UserAId] < [UserBId]");
                });

            migrationBuilder.CreateTable(
                name: "InboxItem",
                schema: "comm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActedOn = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxItem", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelInvite_Invited_Status",
                schema: "comm",
                table: "ChannelInvite",
                columns: new[] { "InvitedUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ChannelInvite_Channel_Invited_Pending",
                schema: "comm",
                table: "ChannelInvite",
                columns: new[] { "ChannelId", "InvitedUserId" },
                unique: true,
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_FriendRequest_Receiver_Status",
                schema: "comm",
                table: "FriendRequest",
                columns: new[] { "ReceiverId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_FriendRequest_Sender_Receiver",
                schema: "comm",
                table: "FriendRequest",
                columns: new[] { "SenderId", "ReceiverId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Friendship_UserA_UserB",
                schema: "comm",
                table: "Friendship",
                columns: new[] { "UserAId", "UserBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InboxItem_User_CreatedAt",
                schema: "comm",
                table: "InboxItem",
                columns: new[] { "UserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_InboxItem_User_IsActedOn",
                schema: "comm",
                table: "InboxItem",
                columns: new[] { "UserId", "IsActedOn" });

            migrationBuilder.Sql(
                "UPDATE [comm].[ChannelRole] SET [Permissions] = 16383 WHERE [IsOwnerRole] = 1;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChannelInvite",
                schema: "comm");

            migrationBuilder.DropTable(
                name: "FriendRequest",
                schema: "comm");

            migrationBuilder.DropTable(
                name: "Friendship",
                schema: "comm");

            migrationBuilder.DropTable(
                name: "InboxItem",
                schema: "comm");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "comm",
                table: "Channel");

            migrationBuilder.DropColumn(
                name: "IconUrl",
                schema: "comm",
                table: "Channel");
        }
    }
}
