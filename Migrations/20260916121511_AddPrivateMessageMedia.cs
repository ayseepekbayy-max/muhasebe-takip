using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MuhasebeTakip2.App.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateMessageMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "PrivateMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "PrivateMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MediaContentType",
                table: "PrivateMessages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MediaKey",
                table: "PrivateMessages",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ViewedAtUtc",
                table: "PrivateMessages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "PrivateMessages");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "PrivateMessages");

            migrationBuilder.DropColumn(
                name: "MediaContentType",
                table: "PrivateMessages");

            migrationBuilder.DropColumn(
                name: "MediaKey",
                table: "PrivateMessages");

            migrationBuilder.DropColumn(
                name: "ViewedAtUtc",
                table: "PrivateMessages");
        }
    }
}
