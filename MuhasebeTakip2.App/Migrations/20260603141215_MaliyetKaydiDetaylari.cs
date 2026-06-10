using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class MaliyetKaydiDetaylari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetayJson",
                table: "MaliyetKayitlari",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Kaynak",
                table: "MaliyetKayitlari",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OkunanMetin",
                table: "MaliyetKayitlari",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetayJson",
                table: "MaliyetKayitlari");

            migrationBuilder.DropColumn(
                name: "Kaynak",
                table: "MaliyetKayitlari");

            migrationBuilder.DropColumn(
                name: "OkunanMetin",
                table: "MaliyetKayitlari");
        }
    }
}
