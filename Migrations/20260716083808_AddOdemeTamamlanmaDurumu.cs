using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddOdemeTamamlanmaDurumu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "SonrakiOdemeTarihi",
                table: "OdemePlanlari",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<bool>(
                name: "TamamlandiMi",
                table: "OdemePlanlari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "TamamlanmaTarihi",
                table: "OdemePlanlari",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"OdemePlanlari\" SET \"TamamlandiMi\" = TRUE, \"SonrakiOdemeTarihi\" = NULL, \"AktifMi\" = FALSE WHERE \"KalanTaksitSayisi\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TamamlandiMi",
                table: "OdemePlanlari");

            migrationBuilder.DropColumn(
                name: "TamamlanmaTarihi",
                table: "OdemePlanlari");

            migrationBuilder.AlterColumn<DateTime>(
                name: "SonrakiOdemeTarihi",
                table: "OdemePlanlari",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
