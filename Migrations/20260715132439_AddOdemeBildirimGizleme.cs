using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddOdemeBildirimGizleme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OdemeBildirimGizlemeleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    KullaniciId = table.Column<int>(type: "integer", nullable: false),
                    OdemePlaniId = table.Column<int>(type: "integer", nullable: false),
                    GizlemeTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OlusturmaTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OlusturanKullaniciAdi = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdemeBildirimGizlemeleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OdemeBildirimGizlemeleri_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OdemeBildirimGizlemeleri_Kullanicilar_KullaniciId",
                        column: x => x.KullaniciId,
                        principalTable: "Kullanicilar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OdemeBildirimGizlemeleri_OdemePlanlari_OdemePlaniId",
                        column: x => x.OdemePlaniId,
                        principalTable: "OdemePlanlari",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OdemeBildirimGizlemeleri_FirmaId",
                table: "OdemeBildirimGizlemeleri",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_OdemeBildirimGizlemeleri_FirmaId_KullaniciId_OdemePlaniId_G~",
                table: "OdemeBildirimGizlemeleri",
                columns: new[] { "FirmaId", "KullaniciId", "OdemePlaniId", "GizlemeTarihi" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OdemeBildirimGizlemeleri_KullaniciId",
                table: "OdemeBildirimGizlemeleri",
                column: "KullaniciId");

            migrationBuilder.CreateIndex(
                name: "IX_OdemeBildirimGizlemeleri_OdemePlaniId",
                table: "OdemeBildirimGizlemeleri",
                column: "OdemePlaniId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OdemeBildirimGizlemeleri");
        }
    }
}
