using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddCekOdemeDurumuVeBildirimleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OdemeTarihi",
                table: "Cekler",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OdendiMi",
                table: "Cekler",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OdemeTarihi",
                table: "Cekler");

            migrationBuilder.DropColumn(
                name: "OdendiMi",
                table: "Cekler");
        }
    }
}
