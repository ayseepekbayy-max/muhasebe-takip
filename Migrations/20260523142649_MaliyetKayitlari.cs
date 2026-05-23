using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class MaliyetKayitlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaliyetKayitlari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    UretimAdi = table.Column<string>(type: "text", nullable: false),
                    UretimAdedi = table.Column<decimal>(type: "numeric", nullable: false),
                    PlakaMaliyeti = table.Column<decimal>(type: "numeric", nullable: false),
                    BantlamaMaliyeti = table.Column<decimal>(type: "numeric", nullable: false),
                    ArkalikMaliyeti = table.Column<decimal>(type: "numeric", nullable: false),
                    MalzemeMaliyeti = table.Column<decimal>(type: "numeric", nullable: false),
                    ToplamMaliyet = table.Column<decimal>(type: "numeric", nullable: false),
                    BirimMaliyet = table.Column<decimal>(type: "numeric", nullable: false),
                    HesapTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaliyetKayitlari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaliyetKayitlari_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaliyetKayitlari_FirmaId",
                table: "MaliyetKayitlari",
                column: "FirmaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaliyetKayitlari");
        }
    }
}
