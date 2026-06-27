using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class SatisHazirlikArsivAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Faturalar_FirmaId",
                table: "Faturalar");

            migrationBuilder.AddColumn<bool>(
                name: "AktifMi",
                table: "StokUrunler",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ArsivNotu",
                table: "StokUrunler",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArsivTarihi",
                table: "StokUrunler",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AktifMi",
                table: "Musteriler",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ArsivNotu",
                table: "Musteriler",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArsivTarihi",
                table: "Musteriler",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AktifMi",
                table: "Faturalar",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ArsivNotu",
                table: "Faturalar",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArsivTarihi",
                table: "Faturalar",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AktifMi",
                table: "CariKartlar",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ArsivNotu",
                table: "CariKartlar",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArsivTarihi",
                table: "CariKartlar",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Faturalar_FirmaId_FaturaNo",
                table: "Faturalar",
                columns: new[] { "FirmaId", "FaturaNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Faturalar_FirmaId_FaturaNo",
                table: "Faturalar");

            migrationBuilder.DropColumn(
                name: "AktifMi",
                table: "StokUrunler");

            migrationBuilder.DropColumn(
                name: "ArsivNotu",
                table: "StokUrunler");

            migrationBuilder.DropColumn(
                name: "ArsivTarihi",
                table: "StokUrunler");

            migrationBuilder.DropColumn(
                name: "AktifMi",
                table: "Musteriler");

            migrationBuilder.DropColumn(
                name: "ArsivNotu",
                table: "Musteriler");

            migrationBuilder.DropColumn(
                name: "ArsivTarihi",
                table: "Musteriler");

            migrationBuilder.DropColumn(
                name: "AktifMi",
                table: "Faturalar");

            migrationBuilder.DropColumn(
                name: "ArsivNotu",
                table: "Faturalar");

            migrationBuilder.DropColumn(
                name: "ArsivTarihi",
                table: "Faturalar");

            migrationBuilder.DropColumn(
                name: "AktifMi",
                table: "CariKartlar");

            migrationBuilder.DropColumn(
                name: "ArsivNotu",
                table: "CariKartlar");

            migrationBuilder.DropColumn(
                name: "ArsivTarihi",
                table: "CariKartlar");

            migrationBuilder.CreateIndex(
                name: "IX_Faturalar_FirmaId",
                table: "Faturalar",
                column: "FirmaId");
        }
    }
}
