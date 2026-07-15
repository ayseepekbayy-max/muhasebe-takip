using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddOdemeEmailBildirimSistemi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OlusturanKullaniciAdi",
                table: "OdemePlanlari",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OlusturanKullaniciId",
                table: "OdemePlanlari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailDogrulandiMi",
                table: "Kullanicilar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "OdemeEmailBildirimiAktifMi",
                table: "Kullanicilar",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "OdemeBildirimGecmisleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    KullaniciId = table.Column<int>(type: "integer", nullable: false),
                    OdemePlaniId = table.Column<int>(type: "integer", nullable: false),
                    BildirimTuru = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OdemeDonemi = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    HedefEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    BasariliMi = table.Column<bool>(type: "boolean", nullable: false),
                    HataMesaji = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BildirimTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OlusturmaTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdemeBildirimGecmisleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OdemeBildirimGecmisleri_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OdemeBildirimGecmisleri_Kullanicilar_KullaniciId",
                        column: x => x.KullaniciId,
                        principalTable: "Kullanicilar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OdemeBildirimGecmisleri_OdemePlanlari_OdemePlaniId",
                        column: x => x.OdemePlaniId,
                        principalTable: "OdemePlanlari",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OdemeBildirimGecmisleri_FirmaId",
                table: "OdemeBildirimGecmisleri",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_OdemeBildirimGecmisleri_FirmaId_KullaniciId_OdemePlaniId_Bi~",
                table: "OdemeBildirimGecmisleri",
                columns: new[] { "FirmaId", "KullaniciId", "OdemePlaniId", "BildirimTuru", "OdemeDonemi" });

            migrationBuilder.CreateIndex(
                name: "IX_OdemeBildirimGecmisleri_FirmaId_OdemePlaniId_BildirimTarihi",
                table: "OdemeBildirimGecmisleri",
                columns: new[] { "FirmaId", "OdemePlaniId", "BildirimTarihi" });

            migrationBuilder.CreateIndex(
                name: "IX_OdemeBildirimGecmisleri_KullaniciId",
                table: "OdemeBildirimGecmisleri",
                column: "KullaniciId");

            migrationBuilder.CreateIndex(
                name: "IX_OdemeBildirimGecmisleri_OdemePlaniId",
                table: "OdemeBildirimGecmisleri",
                column: "OdemePlaniId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OdemeBildirimGecmisleri");

            migrationBuilder.DropColumn(
                name: "OlusturanKullaniciAdi",
                table: "OdemePlanlari");

            migrationBuilder.DropColumn(
                name: "OlusturanKullaniciId",
                table: "OdemePlanlari");

            migrationBuilder.DropColumn(
                name: "EmailDogrulandiMi",
                table: "Kullanicilar");

            migrationBuilder.DropColumn(
                name: "OdemeEmailBildirimiAktifMi",
                table: "Kullanicilar");
        }
    }
}
