using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateMessageVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MessagesClearedAtUtc",
                table: "PrivatePresence",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrivateMessageHidden",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PersonNumber = table.Column<int>(type: "integer", nullable: false),
                    PrivateMessageId = table.Column<int>(type: "integer", nullable: false),
                    HiddenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateMessageHidden", x => x.Id);
                    table.CheckConstraint("CK_PrivateMessageHidden_PersonNumber", "\"PersonNumber\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_PrivateMessageHidden_PrivateMessages_PrivateMessageId",
                        column: x => x.PrivateMessageId,
                        principalTable: "PrivateMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMessageHidden_PersonNumber_PrivateMessageId",
                table: "PrivateMessageHidden",
                columns: new[] { "PersonNumber", "PrivateMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateMessageHidden_PrivateMessageId",
                table: "PrivateMessageHidden",
                column: "PrivateMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrivateMessageHidden");

            migrationBuilder.DropColumn(
                name: "MessagesClearedAtUtc",
                table: "PrivatePresence");
        }
    }
}
