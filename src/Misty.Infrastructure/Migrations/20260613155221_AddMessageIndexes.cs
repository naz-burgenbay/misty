using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Message_ChannelId_CreatedAt_Id",
                schema: "msg",
                table: "Message",
                columns: new[] { "ChannelId", "CreatedAt", "Id" },
                descending: new[] { false, true, true },
                filter: "[ChannelId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Message_ConversationId_CreatedAt_Id",
                schema: "msg",
                table: "Message",
                columns: new[] { "ConversationId", "CreatedAt", "Id" },
                descending: new[] { false, true, true },
                filter: "[ConversationId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Message_ChannelId_CreatedAt_Id",
                schema: "msg",
                table: "Message");

            migrationBuilder.DropIndex(
                name: "IX_Message_ConversationId_CreatedAt_Id",
                schema: "msg",
                table: "Message");
        }
    }
}
