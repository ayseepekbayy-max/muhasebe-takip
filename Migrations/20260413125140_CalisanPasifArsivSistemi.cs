using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class CalisanPasifArsivSistemi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AktifMi",
                table: "Calisanlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AyrilisNotu",
                table: "Calisanlar",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AyrilisTarihi",
                table: "Calisanlar",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AktifMi",
                table: "Calisanlar");

            migrationBuilder.DropColumn(
                name: "AyrilisNotu",
                table: "Calisanlar");

            migrationBuilder.DropColumn(
                name: "AyrilisTarihi",
                table: "Calisanlar");
        }
    }
}
