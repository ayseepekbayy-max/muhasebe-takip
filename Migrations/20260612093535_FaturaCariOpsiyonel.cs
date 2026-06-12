using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class FaturaCariOpsiyonel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Faturalar_CariKartlar_CariKartId",
                table: "Faturalar");

            migrationBuilder.AlterColumn<int>(
                name: "CariKartId",
                table: "Faturalar",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_Faturalar_CariKartlar_CariKartId",
                table: "Faturalar",
                column: "CariKartId",
                principalTable: "CariKartlar",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Faturalar_CariKartlar_CariKartId",
                table: "Faturalar");

            migrationBuilder.AlterColumn<int>(
                name: "CariKartId",
                table: "Faturalar",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Faturalar_CariKartlar_CariKartId",
                table: "Faturalar",
                column: "CariKartId",
                principalTable: "CariKartlar",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
