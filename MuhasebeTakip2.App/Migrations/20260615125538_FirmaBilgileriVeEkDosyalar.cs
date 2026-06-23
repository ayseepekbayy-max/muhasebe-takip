using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class FirmaBilgileriVeEkDosyalar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Adres",
                table: "Firmalar",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Firmalar",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LogoYolu",
                table: "Firmalar",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Telefon",
                table: "Firmalar",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VergiDairesi",
                table: "Firmalar",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VergiNo",
                table: "Firmalar",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "EkDosyalar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    FaturaId = table.Column<int>(type: "integer", nullable: true),
                    CariKartId = table.Column<int>(type: "integer", nullable: true),
                    DosyaAdi = table.Column<string>(type: "text", nullable: false),
                    DosyaYolu = table.Column<string>(type: "text", nullable: false),
                    IcerikTipi = table.Column<string>(type: "text", nullable: false),
                    Boyut = table.Column<long>(type: "bigint", nullable: false),
                    YuklemeTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EkDosyalar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EkDosyalar_CariKartlar_CariKartId",
                        column: x => x.CariKartId,
                        principalTable: "CariKartlar",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EkDosyalar_Faturalar_FaturaId",
                        column: x => x.FaturaId,
                        principalTable: "Faturalar",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EkDosyalar_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EkDosyalar_CariKartId",
                table: "EkDosyalar",
                column: "CariKartId");

            migrationBuilder.CreateIndex(
                name: "IX_EkDosyalar_FaturaId",
                table: "EkDosyalar",
                column: "FaturaId");

            migrationBuilder.CreateIndex(
                name: "IX_EkDosyalar_FirmaId",
                table: "EkDosyalar",
                column: "FirmaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EkDosyalar");

            migrationBuilder.DropColumn(
                name: "Adres",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "LogoYolu",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "Telefon",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "VergiDairesi",
                table: "Firmalar");

            migrationBuilder.DropColumn(
                name: "VergiNo",
                table: "Firmalar");
        }
    }
}
