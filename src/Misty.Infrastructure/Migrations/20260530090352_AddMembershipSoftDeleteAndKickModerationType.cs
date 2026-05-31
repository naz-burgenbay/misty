using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMembershipSoftDeleteAndKickModerationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Membership_Channel_User",
                schema: "comm",
                table: "Membership");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                schema: "comm",
                table: "Membership",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                schema: "comm",
                table: "Membership",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "UX_Membership_Channel_User",
                schema: "comm",
                table: "Membership",
                columns: new[] { "ChannelId", "UserId" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Membership_Channel_User",
                schema: "comm",
                table: "Membership");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                schema: "comm",
                table: "Membership");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                schema: "comm",
                table: "Membership");

            migrationBuilder.CreateIndex(
                name: "UX_Membership_Channel_User",
                schema: "comm",
                table: "Membership",
                columns: new[] { "ChannelId", "UserId" },
                unique: true);
        }
    }
}
