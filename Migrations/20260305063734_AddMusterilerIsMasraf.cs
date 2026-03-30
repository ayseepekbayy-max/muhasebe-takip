using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddMusterilerIsMasraf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Musteriler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdSoyad = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Telefon = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Adres = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Musteriler", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MusteriIsler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MusteriId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tarih = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsAdi = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Gelir = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MusteriIsler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MusteriIsler_Musteriler_MusteriId",
                        column: x => x.MusteriId,
                        principalTable: "Musteriler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MusteriMasraflar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MusteriIsId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tarih = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Aciklama = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Tutar = table.Column<decimal>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MusteriMasraflar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MusteriMasraflar_MusteriIsler_MusteriIsId",
                        column: x => x.MusteriIsId,
                        principalTable: "MusteriIsler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MusteriIsler_MusteriId",
                table: "MusteriIsler",
                column: "MusteriId");

            migrationBuilder.CreateIndex(
                name: "IX_MusteriMasraflar_MusteriIsId",
                table: "MusteriMasraflar",
                column: "MusteriIsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MusteriMasraflar");

            migrationBuilder.DropTable(
                name: "MusteriIsler");

            migrationBuilder.DropTable(
                name: "Musteriler");
        }
    }
}
