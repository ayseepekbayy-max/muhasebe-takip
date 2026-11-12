using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261106090000_AddAdminCompanyTransitions")]
public sealed class AddAdminCompanyTransitions : AdminCompanyTransitionsMigration { }

[DbContext(typeof(SqliteAppDbContext))]
[Migration("20261106090000_AddAdminCompanyTransitions")]
public sealed class AddSqliteAdminCompanyTransitions : AdminCompanyTransitionsMigration { }

public abstract class AdminCompanyTransitionsMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AdminCompanyTransitions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                AdminUserId = table.Column<int>(nullable: false),
                LoginEpoch = table.Column<string>(maxLength: 32, nullable: false),
                SourceCompanyId = table.Column<int>(nullable: false),
                TargetCompanyId = table.Column<int>(nullable: false),
                SourceVersion = table.Column<Guid>(nullable: false),
                ReturnHome = table.Column<bool>(nullable: false),
                Status = table.Column<int>(nullable: false),
                RequestedAtUtc = table.Column<DateTime>(nullable: false),
                ResolvedAtUtc = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_AdminCompanyTransitions", x => x.Id));
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "AdminCompanyTransitions");
}
