using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations;

public partial class AddTimedPrivatePhotos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("PhotoViewHours", "PrivateMessages", type: "integer", nullable: true);
        migrationBuilder.AddColumn<DateTime>("PhotoAccessExpiresAtUtc", "PrivateMessages", type: "timestamp with time zone", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("PhotoViewHours", "PrivateMessages");
        migrationBuilder.DropColumn("PhotoAccessExpiresAtUtc", "PrivateMessages");
    }
}
