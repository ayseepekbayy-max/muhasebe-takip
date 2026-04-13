using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class CalisanArsivEklendi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalisanArsivleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    EskiCalisanId = table.Column<int>(type: "integer", nullable: false),
                    AdSoyad = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Telefon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Maas = table.Column<decimal>(type: "numeric", nullable: false),
                    IseGirisTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AyrilisTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AyrilisNotu = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalisanArsivleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalisanArsivleri_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalisanArsivleri_FirmaId",
                table: "CalisanArsivleri",
                column: "FirmaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalisanArsivleri");
        }
    }
}
