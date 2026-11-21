using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MuhasebeTakip2.App.Data;

namespace MuhasebeTakip2.App.Migrations;

public sealed partial class PreserveEmployeeSalaryIdentity : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        if (ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            throw new NotSupportedException("Reviewed PostgreSQL migration only.");
        // Fail closed; no owner guessing, row deletion, ID rewrite or unrelated history repair.
        m.Sql("""
            DO $$ BEGIN
              IF EXISTS(SELECT FROM public."Calisanlar" WHERE "FirmaId" IS NULL)
                 OR EXISTS(SELECT FROM public."CalisanAvanslari" WHERE "FirmaId" IS NULL) THEN
                RAISE EXCEPTION 'Employee company ownership requires explicit review';
              END IF;
            END $$;
            """);
        m.DropForeignKey("FK_CalisanAvanslari_Calisanlar_CalisanId", "CalisanAvanslari");
        m.DropForeignKey("FK_CalisanPuantajlari_Calisanlar_CalisanId", "CalisanPuantajlari");
        m.AlterColumn<int>("FirmaId", "Calisanlar", nullable: false, oldClrType: typeof(int), oldNullable: true);
        m.AlterColumn<int>("FirmaId", "CalisanAvanslari", nullable: false, oldClrType: typeof(int), oldNullable: true);
        m.AddUniqueConstraint("AK_Calisanlar_Id_FirmaId", "Calisanlar", new[] { "Id", "FirmaId" });
        m.AddColumn<int>("DogrulanmisCalisanId", "CalisanMaasArsivleri", nullable: true);
        m.CreateIndex("IX_CalisanAvanslari_CalisanId_FirmaId", "CalisanAvanslari", new[] { "CalisanId", "FirmaId" });
        m.CreateIndex("IX_CalisanPuantajlari_CalisanId_FirmaId", "CalisanPuantajlari", new[] { "CalisanId", "FirmaId" });
        m.CreateIndex("IX_CalisanMaasArsivleri_DogrulanmisCalisanId_FirmaId", "CalisanMaasArsivleri", new[] { "DogrulanmisCalisanId", "FirmaId" });
        m.AddForeignKey("FK_CalisanAvanslari_Calisanlar_CalisanId_FirmaId", "CalisanAvanslari", new[] { "CalisanId", "FirmaId" }, "Calisanlar", principalColumns: new[] { "Id", "FirmaId" }, onDelete: ReferentialAction.Restrict);
        m.AddForeignKey("FK_CalisanPuantajlari_Calisanlar_CalisanId_FirmaId", "CalisanPuantajlari", new[] { "CalisanId", "FirmaId" }, "Calisanlar", principalColumns: new[] { "Id", "FirmaId" }, onDelete: ReferentialAction.Restrict);
        m.AddForeignKey("FK_CalisanMaasArsivleri_Calisanlar_DogrulanmisCalisanId_FirmaId", "CalisanMaasArsivleri", new[] { "DogrulanmisCalisanId", "FirmaId" }, "Calisanlar", principalColumns: new[] { "Id", "FirmaId" }, onDelete: ReferentialAction.Restrict);
        // Preserve an already valid same-ID/same-company relation only. Never use a name.
        // Existing original CalisanId, amounts, periods and dates remain untouched.
        m.Sql("""
            UPDATE public."CalisanMaasArsivleri" s SET "DogrulanmisCalisanId"=s."CalisanId"
            FROM public."Calisanlar" c WHERE c."Id">0 AND c."Id"=s."CalisanId" AND c."FirmaId"=s."FirmaId";
            """);
    }
    protected override void Down(MigrationBuilder m)
    {
        // A reviewed restore/mapping must not silently lose its verified identity on rollback.
        throw new NotSupportedException("Forward-only identity protection. Review/export verified links before rollback; never drop them automatically.");
    }
}
