using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Misty.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attachment",
                schema: "msg",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerType = table.Column<int>(type: "int", nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AvatarUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChannelIconChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BlobContainer = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CdnUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachment", x => x.Id);
                    table.CheckConstraint("CK_Attachment_ExactlyOneOwner", "(([OwnerType] = 1 AND [MessageId] IS NOT NULL AND [AvatarUserId] IS NULL AND [ChannelIconChannelId] IS NULL) OR ([OwnerType] = 2 AND [MessageId] IS NULL AND [AvatarUserId] IS NOT NULL AND [ChannelIconChannelId] IS NULL) OR ([OwnerType] = 3 AND [MessageId] IS NULL AND [AvatarUserId] IS NULL AND [ChannelIconChannelId] IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_Attachment_Message_MessageId",
                        column: x => x.MessageId,
                        principalSchema: "msg",
                        principalTable: "Message",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_MessageId",
                schema: "msg",
                table: "Attachment",
                column: "MessageId",
                filter: "[MessageId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachment",
                schema: "msg");
        }
    }
}
