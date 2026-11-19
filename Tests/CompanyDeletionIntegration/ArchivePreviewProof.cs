using System.Reflection;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Pages.SuperAdmin;
using MuhasebeTakip2.App.Services;

internal static class ArchivePreviewProof
{
    private const string Connection="Host=/work;Database=firmova_stage10_synthetic;Username=firmova_app_runtime;Pooling=false";
    private static AppDbContext Context(bool readOnly=false)=>new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(Connection+(readOnly?";Options=-c default_transaction_read_only=on":"")).Options);
    internal static async Task RunAsync()
    {
        if(!OperatingSystem.IsLinux()) throw new InvalidOperationException("Isolated Linux fixture only.");
        await using var writer=Context();
        if(await writer.Firmalar.AnyAsync()) throw new InvalidDataException("Expected empty synthetic fixture.");
        writer.AddRange(new Firma{Id=1,FirmaAdi="Protected synthetic"},new Firma{Id=72,FirmaAdi="Administrator synthetic"},
            new Firma{Id=73,FirmaAdi="Preview target"},new Firma{Id=74,FirmaAdi="Other synthetic"});
        writer.AddRange(new Calisan{Id=301,FirmaId=73,AdSoyad="Target",IseGirisTarihi=DateTime.UtcNow},
            new Calisan{Id=401,FirmaId=74,AdSoyad="Other",IseGirisTarihi=DateTime.UtcNow});
        await writer.SaveChangesAsync();
        await InsertArchive(writer,901,73,301);
        await InsertArchive(writer,902,73,302); // Historical employee no longer exists: retain FirmaId ownership.
        await InsertArchive(writer,903,74,401);
        await InsertArchive(writer,904,72,501);
        await InsertArchive(writer,905,1,601);
        await using var reader=Context(readOnly:true);
        var before=await Digest(reader);
        var actual=await new CompanyDeletionInventory(reader).InspectAsync(73,default);
        var deletion=(string[])typeof(PostgreSqlCompanyDeletion).GetField("Tables",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var names=actual.Tables.Select(x=>x.Name).ToArray();
        Check(names.Length==28 && names.Distinct(StringComparer.Ordinal).Count()==28 &&
            deletion.ToHashSet(StringComparer.Ordinal).SetEquals(names),"exact 28-table scope, no omissions or extras");
        var archive=actual.Tables.Single(x=>x.Name=="CalisanArsivleri");
        Check(archive.Rows==2 && !archive.HasExternalReferences,"target-only archives counted, absent historical employee not guessed");
        var other=await new CompanyDeletionInventory(reader).InspectAsync(74,default);
        Check(other.Tables.Single(x=>x.Name=="CalisanArsivleri").Rows==1,"other company has independent archive count");
        Check(await Digest(reader)==before,"preview makes no business-row changes under database READ ONLY");
        Console.WriteLine("PASS real PostgreSQL inventory matches deletion scope 28/28 and counts archives by selected FirmaId");

        // Cross-company links must block rather than borrow the current employee's ownership.
        await writer.Database.ExecuteSqlRawAsync("""UPDATE public."CalisanArsivleri" SET "EskiCalisanId"=401 WHERE "Id"=902""");
        actual=await new CompanyDeletionInventory(reader).InspectAsync(73,default);
        Check(actual.Tables.Single(x=>x.Name=="CalisanArsivleri").HasExternalReferences,"foreign current employee is surfaced as blocker");
        await writer.Database.ExecuteSqlRawAsync("""UPDATE public."CalisanArsivleri" SET "EskiCalisanId"=302 WHERE "Id"=902""");
        await writer.Database.ExecuteSqlRawAsync("""UPDATE public."CalisanArsivleri" SET "EskiCalisanId"=301 WHERE "Id"=903""");
        actual=await new CompanyDeletionInventory(reader).InspectAsync(73,default);
        Check(actual.Tables.Single(x=>x.Name=="Calisanlar").HasExternalReferences,"other-company archive referencing target employee is surfaced");
        await writer.Database.ExecuteSqlRawAsync("""UPDATE public."CalisanArsivleri" SET "EskiCalisanId"=401 WHERE "Id"=903""");
        await writer.Database.ExecuteSqlRawAsync("""DELETE FROM public."CalisanArsivleri" WHERE "FirmaId"=73""");
        actual=await new CompanyDeletionInventory(reader).InspectAsync(73,default);
        Check(actual.Tables.Count==28 && actual.Tables.Single(x=>x.Name=="CalisanArsivleri").Rows==0,"empty archive remains represented in the scope");
        Console.WriteLine("PASS empty archive and both directions of historical cross-company references handled safely");
        Check(!FirmaSilmeOnizlemeModel.HedefUygun(1,72) && !FirmaSilmeOnizlemeModel.HedefUygun(72,72) &&
            FirmaSilmeOnizlemeModel.HedefUygun(73,72),"protected target predicate unchanged");
        Check(new LegacyCompanyFileReport.Report([],1,0,0,0,0).HasPrivatePanelData,"private inventory still reports separately protected data");
        Check(await writer.Firmalar.CountAsync()==4 && await writer.Database.SqlQueryRaw<int>("""SELECT count(*)::integer AS "Value" FROM public."CalisanArsivleri" WHERE "FirmaId" IN(1,72,74)""").SingleAsync()==3,
            "protected and other-company rows retained");
        try{await new CompanyDeletionInventory(reader).InspectAsync(1,default);throw new InvalidDataException("ID1 accepted");}
        catch(ArgumentOutOfRangeException){}
        Console.WriteLine("PASS ID1/admin target protections, unknown ownership gate and other-company records retained");
    }
    private static async Task InsertArchive(AppDbContext db,int id,int company,int oldEmployee)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO public."CalisanArsivleri"("Id","FirmaId","EskiCalisanId","AdSoyad","Telefon","Maas","IseGirisTarihi","AyrilisTarihi","AyrilisNotu")
            VALUES({id},{company},{oldEmployee},{"Synthetic"},{""},{1m},{DateTime.UtcNow},{DateTime.UtcNow},{"Synthetic"})
            """);
    }
    private static Task<string> Digest(AppDbContext db)=>db.Database.SqlQueryRaw<string>("""
        SELECT md5(string_agg(r::text,chr(10) ORDER BY r::text)) AS "Value" FROM (
          SELECT jsonb_build_array('Firmalar',to_jsonb(f)) r FROM public."Firmalar" f
          UNION ALL SELECT jsonb_build_array('Calisanlar',to_jsonb(c)) FROM public."Calisanlar" c
          UNION ALL SELECT jsonb_build_array('CalisanArsivleri',to_jsonb(a)) FROM public."CalisanArsivleri" a
        ) s
        """).SingleAsync();
    private static void Check(bool result,string description)
    {if(!result)throw new InvalidDataException(description);}
}
