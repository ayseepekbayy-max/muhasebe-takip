using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddCariToKasaHareket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CariKartId",
                table: "KasaHareketleri",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KasaHareketleri_CariKartId",
                table: "KasaHareketleri",
                column: "CariKartId");

            migrationBuilder.AddForeignKey(
                name: "FK_KasaHareketleri_CariKartlar_CariKartId",
                table: "KasaHareketleri",
                column: "CariKartId",
                principalTable: "CariKartlar",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KasaHareketleri_CariKartlar_CariKartId",
                table: "KasaHareketleri");

            migrationBuilder.DropIndex(
                name: "IX_KasaHareketleri_CariKartId",
                table: "KasaHareketleri");

            migrationBuilder.DropColumn(
                name: "CariKartId",
                table: "KasaHareketleri");
        }
    }
}
