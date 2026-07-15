using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddOdemePlaniVeOdemeHareketleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MenuOdemeler",
                table: "Firmalar",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "OdemePlanlari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    OdemeAdi = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    OdemeTuru = table.Column<int>(type: "integer", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AylikOdemeTutari = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ToplamTaksitSayisi = table.Column<int>(type: "integer", nullable: false),
                    KalanTaksitSayisi = table.Column<int>(type: "integer", nullable: false),
                    OdemeGunu = table.Column<int>(type: "integer", nullable: false),
                    IlkOdemeTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SonrakiOdemeTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SonOdemeTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SonOdemeYapildiMi = table.Column<bool>(type: "boolean", nullable: false),
                    BildirimGunu = table.Column<int>(type: "integer", nullable: false),
                    BildirimAktifMi = table.Column<bool>(type: "boolean", nullable: false),
                    OtomatikTaksitDusur = table.Column<bool>(type: "boolean", nullable: false),
                    AktifMi = table.Column<bool>(type: "boolean", nullable: false),
                    OlusturmaTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GuncellemeTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdemePlanlari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OdemePlanlari_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OdemeHareketleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    OdemePlaniId = table.Column<int>(type: "integer", nullable: false),
                    OdemeTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    KalanTaksitSayisi = table.Column<int>(type: "integer", nullable: false),
                    OlusturmaTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OlusturanKullaniciId = table.Column<int>(type: "integer", nullable: true),
                    OlusturanKullaniciAdi = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdemeHareketleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OdemeHareketleri_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OdemeHareketleri_OdemePlanlari_OdemePlaniId",
                        column: x => x.OdemePlaniId,
                        principalTable: "OdemePlanlari",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OdemeHareketleri_FirmaId",
                table: "OdemeHareketleri",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_OdemeHareketleri_FirmaId_OdemePlaniId_OdemeTarihi",
                table: "OdemeHareketleri",
                columns: new[] { "FirmaId", "OdemePlaniId", "OdemeTarihi" });

            migrationBuilder.CreateIndex(
                name: "IX_OdemeHareketleri_OdemePlaniId",
                table: "OdemeHareketleri",
                column: "OdemePlaniId");

            migrationBuilder.CreateIndex(
                name: "IX_OdemePlanlari_FirmaId",
                table: "OdemePlanlari",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_OdemePlanlari_FirmaId_AktifMi_SonrakiOdemeTarihi",
                table: "OdemePlanlari",
                columns: new[] { "FirmaId", "AktifMi", "SonrakiOdemeTarihi" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OdemeHareketleri");

            migrationBuilder.DropTable(
                name: "OdemePlanlari");

            migrationBuilder.DropColumn(
                name: "MenuOdemeler",
                table: "Firmalar");
        }
    }
}
