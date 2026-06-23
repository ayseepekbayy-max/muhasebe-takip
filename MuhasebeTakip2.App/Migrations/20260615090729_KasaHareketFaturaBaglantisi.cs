using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class KasaHareketFaturaBaglantisi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FaturaId",
                table: "KasaHareketleri",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KasaHareketleri_FaturaId",
                table: "KasaHareketleri",
                column: "FaturaId");

            migrationBuilder.AddForeignKey(
                name: "FK_KasaHareketleri_Faturalar_FaturaId",
                table: "KasaHareketleri",
                column: "FaturaId",
                principalTable: "Faturalar",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KasaHareketleri_Faturalar_FaturaId",
                table: "KasaHareketleri");

            migrationBuilder.DropIndex(
                name: "IX_KasaHareketleri_FaturaId",
                table: "KasaHareketleri");

            migrationBuilder.DropColumn(
                name: "FaturaId",
                table: "KasaHareketleri");
        }
    }
}
