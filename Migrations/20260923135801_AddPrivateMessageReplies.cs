using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateMessageReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsReply",
                table: "PrivateMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReplyToMessageId",
                table: "PrivateMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMessages_ReplyToMessageId",
                table: "PrivateMessages",
                column: "ReplyToMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_PrivateMessages_PrivateMessages_ReplyToMessageId",
                table: "PrivateMessages",
                column: "ReplyToMessageId",
                principalTable: "PrivateMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrivateMessages_PrivateMessages_ReplyToMessageId",
                table: "PrivateMessages");

            migrationBuilder.DropIndex(
                name: "IX_PrivateMessages_ReplyToMessageId",
                table: "PrivateMessages");

            migrationBuilder.DropColumn(
                name: "IsReply",
                table: "PrivateMessages");

            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                table: "PrivateMessages");
        }
    }
}
