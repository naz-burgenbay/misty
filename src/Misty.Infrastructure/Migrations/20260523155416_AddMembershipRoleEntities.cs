using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    public partial class AddMembershipRoleEntities : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChannelRole",
                schema: "comm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Permissions = table.Column<long>(type: "bigint", nullable: false),
                    IsOwnerRole = table.Column<bool>(type: "bit", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelRole", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelRole_Channel_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "comm",
                        principalTable: "Channel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Membership",
                schema: "comm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Membership", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Membership_Channel_ChannelId",
                        column: x => x.ChannelId,
                        principalSchema: "comm",
                        principalTable: "Channel",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MemberRole",
                schema: "comm",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemberRole", x => new { x.MembershipId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_MemberRole_ChannelRole_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "comm",
                        principalTable: "ChannelRole",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MemberRole_Membership_MembershipId",
                        column: x => x.MembershipId,
                        principalSchema: "comm",
                        principalTable: "Membership",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChannelRole_ChannelId",
                schema: "comm",
                table: "ChannelRole",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "UX_Membership_Channel_User",
                schema: "comm",
                table: "Membership",
                columns: new[] { "ChannelId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemberRole_RoleId",
                schema: "comm",
                table: "MemberRole",
                column: "RoleId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MemberRole", schema: "comm");
            migrationBuilder.DropTable(name: "Membership", schema: "comm");
            migrationBuilder.DropTable(name: "ChannelRole", schema: "comm");
        }
    }
}
