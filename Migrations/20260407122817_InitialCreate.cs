using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CalisanMaasArsivleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    CalisanId = table.Column<int>(type: "integer", nullable: false),
                    DonemBaslangic = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DonemBitis = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ToplamMaas = table.Column<decimal>(type: "numeric", nullable: false),
                    ToplamAvans = table.Column<decimal>(type: "numeric", nullable: false),
                    KalanMaas = table.Column<decimal>(type: "numeric", nullable: false),
                    OdemeTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Aciklama = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalisanMaasArsivleri", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Firmalar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaAdi = table.Column<string>(type: "text", nullable: false),
                    AktifMi = table.Column<bool>(type: "boolean", nullable: false),
                    MenuCariKartlar = table.Column<bool>(type: "boolean", nullable: false),
                    MenuKasa = table.Column<bool>(type: "boolean", nullable: false),
                    MenuRaporlar = table.Column<bool>(type: "boolean", nullable: false),
                    MenuCalisanlar = table.Column<bool>(type: "boolean", nullable: false),
                    MenuMusteriler = table.Column<bool>(type: "boolean", nullable: false),
                    MenuStoklar = table.Column<bool>(type: "boolean", nullable: false),
                    MenuMaliyet = table.Column<bool>(type: "boolean", nullable: false),
                    MenuCekler = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Firmalar", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Calisanlar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Maas = table.Column<decimal>(type: "numeric", nullable: false),
                    Avans = table.Column<decimal>(type: "numeric", nullable: false),
                    FirmaId = table.Column<int>(type: "integer", nullable: true),
                    Ad = table.Column<string>(type: "text", nullable: false),
                    AdSoyad = table.Column<string>(type: "text", nullable: false),
                    Telefon = table.Column<string>(type: "text", nullable: true),
                    IseGirisTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calisanlar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Calisanlar_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CariKartlar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: true),
                    Ad = table.Column<string>(type: "text", nullable: false),
                    Unvan = table.Column<string>(type: "text", nullable: false),
                    Telefon = table.Column<string>(type: "text", nullable: true),
                    VergiNo = table.Column<string>(type: "text", nullable: true),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    OlusturmaTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CariKartlar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CariKartlar_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Cekler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    No = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tarih = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    ResimYolu = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    OlusturmaTarihi = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cekler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cekler_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Kullanicilar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KullaniciAdi = table.Column<string>(type: "text", nullable: false),
                    Sifre = table.Column<string>(type: "text", nullable: false),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    Rol = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Kullanicilar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Kullanicilar_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Musteriler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: true),
                    Ad = table.Column<string>(type: "text", nullable: false),
                    AdSoyad = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Telefon = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Adres = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Musteriler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Musteriler_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StokUrunler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: true),
                    Ad = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Birim = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StokUrunler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StokUrunler_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CalisanAvanslari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Ad = table.Column<string>(type: "text", nullable: false),
                    FirmaId = table.Column<int>(type: "integer", nullable: true),
                    CalisanId = table.Column<int>(type: "integer", nullable: false),
                    Tarih = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    ArsivlendiMi = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalisanAvanslari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalisanAvanslari_Calisanlar_CalisanId",
                        column: x => x.CalisanId,
                        principalTable: "Calisanlar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalisanAvanslari_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CalisanPuantajlari",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: false),
                    CalisanId = table.Column<int>(type: "integer", nullable: false),
                    Tarih = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    Not = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalisanPuantajlari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalisanPuantajlari_Calisanlar_CalisanId",
                        column: x => x.CalisanId,
                        principalTable: "Calisanlar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CalisanPuantajlari_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KasaHareketleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: true),
                    Tarih = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric", nullable: false),
                    Aciklama = table.Column<string>(type: "text", nullable: false),
                    CariKartId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KasaHareketleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KasaHareketleri_CariKartlar_CariKartId",
                        column: x => x.CariKartId,
                        principalTable: "CariKartlar",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_KasaHareketleri_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MusteriIsler",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: true),
                    Ad = table.Column<string>(type: "text", nullable: false),
                    MusteriId = table.Column<int>(type: "integer", nullable: false),
                    Tarih = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsAdi = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Gelir = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MusteriIsler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MusteriIsler_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MusteriIsler_Musteriler_MusteriId",
                        column: x => x.MusteriId,
                        principalTable: "Musteriler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StokHareketleri",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: true),
                    Ad = table.Column<string>(type: "text", nullable: false),
                    StokUrunId = table.Column<int>(type: "integer", nullable: false),
                    Tarih = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    Miktar = table.Column<decimal>(type: "numeric", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StokHareketleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StokHareketleri_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_StokHareketleri_StokUrunler_StokUrunId",
                        column: x => x.StokUrunId,
                        principalTable: "StokUrunler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MusteriMasraflar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FirmaId = table.Column<int>(type: "integer", nullable: true),
                    Ad = table.Column<string>(type: "text", nullable: false),
                    MusteriIsId = table.Column<int>(type: "integer", nullable: false),
                    Tarih = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MusteriMasraflar", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MusteriMasraflar_Firmalar_FirmaId",
                        column: x => x.FirmaId,
                        principalTable: "Firmalar",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MusteriMasraflar_MusteriIsler_MusteriIsId",
                        column: x => x.MusteriIsId,
                        principalTable: "MusteriIsler",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CalisanAvanslari_CalisanId",
                table: "CalisanAvanslari",
                column: "CalisanId");

            migrationBuilder.CreateIndex(
                name: "IX_CalisanAvanslari_FirmaId",
                table: "CalisanAvanslari",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_Calisanlar_FirmaId",
                table: "Calisanlar",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_CalisanPuantajlari_CalisanId",
                table: "CalisanPuantajlari",
                column: "CalisanId");

            migrationBuilder.CreateIndex(
                name: "IX_CalisanPuantajlari_FirmaId",
                table: "CalisanPuantajlari",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_CariKartlar_FirmaId",
                table: "CariKartlar",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_Cekler_FirmaId",
                table: "Cekler",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_KasaHareketleri_CariKartId",
                table: "KasaHareketleri",
                column: "CariKartId");

            migrationBuilder.CreateIndex(
                name: "IX_KasaHareketleri_FirmaId",
                table: "KasaHareketleri",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_Kullanicilar_FirmaId",
                table: "Kullanicilar",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_MusteriIsler_FirmaId",
                table: "MusteriIsler",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_MusteriIsler_MusteriId",
                table: "MusteriIsler",
                column: "MusteriId");

            migrationBuilder.CreateIndex(
                name: "IX_Musteriler_FirmaId",
                table: "Musteriler",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_MusteriMasraflar_FirmaId",
                table: "MusteriMasraflar",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_MusteriMasraflar_MusteriIsId",
                table: "MusteriMasraflar",
                column: "MusteriIsId");

            migrationBuilder.CreateIndex(
                name: "IX_StokHareketleri_FirmaId",
                table: "StokHareketleri",
                column: "FirmaId");

            migrationBuilder.CreateIndex(
                name: "IX_StokHareketleri_StokUrunId",
                table: "StokHareketleri",
                column: "StokUrunId");

            migrationBuilder.CreateIndex(
                name: "IX_StokUrunler_FirmaId",
                table: "StokUrunler",
                column: "FirmaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalisanAvanslari");

            migrationBuilder.DropTable(
                name: "CalisanMaasArsivleri");

            migrationBuilder.DropTable(
                name: "CalisanPuantajlari");

            migrationBuilder.DropTable(
                name: "Cekler");

            migrationBuilder.DropTable(
                name: "KasaHareketleri");

            migrationBuilder.DropTable(
                name: "Kullanicilar");

            migrationBuilder.DropTable(
                name: "MusteriMasraflar");

            migrationBuilder.DropTable(
                name: "StokHareketleri");

            migrationBuilder.DropTable(
                name: "Calisanlar");

            migrationBuilder.DropTable(
                name: "CariKartlar");

            migrationBuilder.DropTable(
                name: "MusteriIsler");

            migrationBuilder.DropTable(
                name: "StokUrunler");

            migrationBuilder.DropTable(
                name: "Musteriler");

            migrationBuilder.DropTable(
                name: "Firmalar");
        }
    }
}
