using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class FirmaMenuAyarlariniEkle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MenuCalisanlar",
                table: "Firmalar",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MenuCariKartlar",
                table: "Firmalar",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MenuKasa",
                table: "Firmalar",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MenuMaliyet",
                table: "Firmalar",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MenuMusteriler",
                table: "Firmalar",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MenuRaporlar",
                table: "Firmalar",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MenuStoklar",
                table: "Firmalar",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MenuCalisanlar",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "MenuCariKartlar",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "MenuKasa",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "MenuMaliyet",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "MenuMusteriler",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "MenuRaporlar",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "MenuStoklar",
                table: "Firmalar");
        }
    }
}
