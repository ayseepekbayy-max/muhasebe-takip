using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class CalisanMaasArsivEkle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ArsivlendiMi",
                table: "CalisanAvanslari",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CalisanMaasArsivleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FirmaId = table.Column<int>(type: "INTEGER", nullable: false),
                    CalisanId = table.Column<int>(type: "INTEGER", nullable: false),
                    DonemBaslangic = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DonemBitis = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ToplamMaas = table.Column<decimal>(type: "TEXT", nullable: false),
                    ToplamAvans = table.Column<decimal>(type: "TEXT", nullable: false),
                    KalanMaas = table.Column<decimal>(type: "TEXT", nullable: false),
                    OdemeTarihi = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Aciklama = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalisanMaasArsivleri", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalisanMaasArsivleri");

            migrationBuilder.DropColumn(
                name: "ArsivlendiMi",
                table: "CalisanAvanslari");
        }
    }
}
