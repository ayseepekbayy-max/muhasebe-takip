using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddCashBankAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "KasaHesapId",
                table: "KasaHareketleri",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KasaHesaplar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    Ad = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tur = table.Column<int>(type: "integer", nullable: false),
                    AcilisBakiyesi = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AktifMi = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KasaHesaplar", x => x.Id);
                    table.UniqueConstraint("AK_KasaHesaplar_Id_FirmaId", x => new { x.Id, x.FirmaId });
                    table.ForeignKey(
                        name: "FK_KasaHesaplar_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KasaTransferler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    KaynakHesapId = table.Column<int>(type: "integer", nullable: false),
                    HedefHesapId = table.Column<int>(type: "integer", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Tarih = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KasaTransferler", x => x.Id);
                    table.CheckConstraint("CK_KasaTransfer_Hesaplar", "\"KaynakHesapId\" <> \"HedefHesapId\"");
                    table.CheckConstraint("CK_KasaTransfer_Tutar", "\"Tutar\" > 0");
                    table.ForeignKey(
                        name: "FK_KasaTransferler_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KasaTransferler_KasaHesaplar_HedefHesapId_FirmaId",
                        columns: x => new { x.HedefHesapId, x.FirmaId },
                        principalTable: "KasaHesaplar",
                        principalColumns: new[] { "Id", "FirmaId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KasaTransferler_KasaHesaplar_KaynakHesapId_FirmaId",
                        columns: x => new { x.KaynakHesapId, x.FirmaId },
                        principalTable: "KasaHesaplar",
                        principalColumns: new[] { "Id", "FirmaId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KasaHareketleri_KasaHesapId_FirmaId",
                table: "KasaHareketleri",
                columns: new[] { "KasaHesapId", "FirmaId" });

            migrationBuilder.CreateIndex(
                name: "IX_KasaHesaplar_FirmaId_Ad",
                table: "KasaHesaplar",
                columns: new[] { "FirmaId", "Ad" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KasaTransferler_FirmaId_Tarih",
                table: "KasaTransferler",
                columns: new[] { "FirmaId", "Tarih" });

            migrationBuilder.CreateIndex(
                name: "IX_KasaTransferler_HedefHesapId_FirmaId",
                table: "KasaTransferler",
                columns: new[] { "HedefHesapId", "FirmaId" });

            migrationBuilder.CreateIndex(
                name: "IX_KasaTransferler_KaynakHesapId_FirmaId",
                table: "KasaTransferler",
                columns: new[] { "KaynakHesapId", "FirmaId" });

            migrationBuilder.AddForeignKey(
                name: "FK_KasaHareketleri_KasaHesaplar_KasaHesapId_FirmaId",
                table: "KasaHareketleri",
                columns: new[] { "KasaHesapId", "FirmaId" },
                principalTable: "KasaHesaplar",
                principalColumns: new[] { "Id", "FirmaId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KasaHareketleri_KasaHesaplar_KasaHesapId_FirmaId",
                table: "KasaHareketleri");

            migrationBuilder.DropTable(
                name: "KasaTransferler");

            migrationBuilder.DropTable(
                name: "KasaHesaplar");

            migrationBuilder.DropIndex(
                name: "IX_KasaHareketleri_KasaHesapId_FirmaId",
                table: "KasaHareketleri");

            migrationBuilder.DropColumn(
                name: "KasaHesapId",
                table: "KasaHareketleri");
        }
    }
}
