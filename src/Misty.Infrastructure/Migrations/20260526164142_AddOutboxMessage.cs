using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessage",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessage_Unpublished_CreatedAt",
                schema: "msg",
                table: "OutboxMessage",
                column: "CreatedAt",
                filter: "[PublishedAt] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxMessage",
                schema: "msg");
        }
    }
}
