using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class FirmaAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FirmaId",
                table: "StokUrunler",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirmaId",
                table: "StokHareketleri",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirmaId",
                table: "MusteriMasraflar",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirmaId",
                table: "Musteriler",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirmaId",
                table: "MusteriIsler",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirmaId",
                table: "KasaHareketleri",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirmaId",
                table: "CariKartlar",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirmaId",
                table: "Calisanlar",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FirmaId",
                table: "CalisanAvanslari",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StokUrunler_FirmaId",
                table: "StokUrunler",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_StokHareketleri_FirmaId",
                table: "StokHareketleri",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_MusteriMasraflar_FirmaId",
                table: "MusteriMasraflar",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_Musteriler_FirmaId",
                table: "Musteriler",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_MusteriIsler_FirmaId",
                table: "MusteriIsler",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_KasaHareketleri_FirmaId",
                table: "KasaHareketleri",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_CariKartlar_FirmaId",
                table: "CariKartlar",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_Calisanlar_FirmaId",
                table: "Calisanlar",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_CalisanAvanslari_FirmaId",
                table: "CalisanAvanslari",
                column: "FirmaId");

            migrationBuilder.AddForeignKey(
                name: "FK_CalisanAvanslari_Firmalar_FirmaId",
                table: "CalisanAvanslari",
                column: "FirmaId",
                principalTable: "Firmalar",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Calisanlar_Firmalar_FirmaId",
                table: "Calisanlar",
                column: "FirmaId",
                principalTable: "Firmalar",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_CariKartlar_Firmalar_FirmaId",
                table: "CariKartlar",
                column: "FirmaId",
                principalTable: "Firmalar",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_KasaHareketleri_Firmalar_FirmaId",
                table: "KasaHareketleri",
                column: "FirmaId",
                principalTable: "Firmalar",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MusteriIsler_Firmalar_FirmaId",
                table: "MusteriIsler",
                column: "FirmaId",
                principalTable: "Firmalar",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Musteriler_Firmalar_FirmaId",
                table: "Musteriler",
                column: "FirmaId",
                principalTable: "Firmalar",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MusteriMasraflar_Firmalar_FirmaId",
                table: "MusteriMasraflar",
                column: "FirmaId",
                principalTable: "Firmalar",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StokHareketleri_Firmalar_FirmaId",
                table: "StokHareketleri",
                column: "FirmaId",
                principalTable: "Firmalar",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StokUrunler_Firmalar_FirmaId",
                table: "StokUrunler",
                column: "FirmaId",
                principalTable: "Firmalar",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CalisanAvanslari_Firmalar_FirmaId",
                table: "CalisanAvanslari");

            migrationBuilder.DropForeignKey(
                name: "FK_Calisanlar_Firmalar_FirmaId",
                table: "Calisanlar");

            migrationBuilder.DropForeignKey(
                name: "FK_CariKartlar_Firmalar_FirmaId",
                table: "CariKartlar");

            migrationBuilder.DropForeignKey(
                name: "FK_KasaHareketleri_Firmalar_FirmaId",
                table: "KasaHareketleri");

            migrationBuilder.DropForeignKey(
                name: "FK_MusteriIsler_Firmalar_FirmaId",
                table: "MusteriIsler");

            migrationBuilder.DropForeignKey(
                name: "FK_Musteriler_Firmalar_FirmaId",
                table: "Musteriler");

            migrationBuilder.DropForeignKey(
                name: "FK_MusteriMasraflar_Firmalar_FirmaId",
                table: "MusteriMasraflar");

            migrationBuilder.DropForeignKey(
                name: "FK_StokHareketleri_Firmalar_FirmaId",
                table: "StokHareketleri");

            migrationBuilder.DropForeignKey(
                name: "FK_StokUrunler_Firmalar_FirmaId",
                table: "StokUrunler");

            migrationBuilder.DropIndex(
                name: "IX_StokUrunler_FirmaId",
                table: "StokUrunler");

            migrationBuilder.DropIndex(
                name: "IX_StokHareketleri_FirmaId",
                table: "StokHareketleri");

            migrationBuilder.DropIndex(
                name: "IX_MusteriMasraflar_FirmaId",
                table: "MusteriMasraflar");

            migrationBuilder.DropIndex(
                name: "IX_Musteriler_FirmaId",
                table: "Musteriler");

            migrationBuilder.DropIndex(
                name: "IX_MusteriIsler_FirmaId",
                table: "MusteriIsler");

            migrationBuilder.DropIndex(
                name: "IX_KasaHareketleri_FirmaId",
                table: "KasaHareketleri");

            migrationBuilder.DropIndex(
                name: "IX_CariKartlar_FirmaId",
                table: "CariKartlar");

            migrationBuilder.DropIndex(
                name: "IX_Calisanlar_FirmaId",
                table: "Calisanlar");

            migrationBuilder.DropIndex(
                name: "IX_CalisanAvanslari_FirmaId",
                table: "CalisanAvanslari");

            migrationBuilder.DropColumn(
                name: "FirmaId",
                table: "StokUrunler");

            migrationBuilder.DropColumn(
                name: "FirmaId",
                table: "StokHareketleri");

            migrationBuilder.DropColumn(
                name: "FirmaId",
                table: "MusteriMasraflar");

            migrationBuilder.DropColumn(
                name: "FirmaId",
                table: "Musteriler");

            migrationBuilder.DropColumn(
                name: "FirmaId",
                table: "MusteriIsler");

            migrationBuilder.DropColumn(
                name: "FirmaId",
                table: "KasaHareketleri");

            migrationBuilder.DropColumn(
                name: "FirmaId",
                table: "CariKartlar");

            migrationBuilder.DropColumn(
                name: "FirmaId",
                table: "Calisanlar");

            migrationBuilder.DropColumn(
                name: "FirmaId",
                table: "CalisanAvanslari");
        }
    }
}
