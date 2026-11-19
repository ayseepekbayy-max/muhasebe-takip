using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

internal static class CompanyAuditActorProof
{
    private const string Runtime="Host=/work;Database=firmova_stage10_synthetic;Username=firmova_app_runtime;Pooling=false";
    private static AppDbContext Context(bool owner=false, bool readOnly=false)=>new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql((owner?Runtime.Replace("Username=firmova_app_runtime","Username=postgres"):Runtime)+(readOnly?";Options=-c default_transaction_read_only=on":"")).Options);
    internal static async Task RunAsync()
    {
        if(!OperatingSystem.IsLinux())throw new InvalidOperationException("Synthetic Linux fixture only.");
        await using var setup=Context();
        Check(await setup.Firmalar.CountAsync()==4 && await setup.Kullanicilar.CountAsync()==1,"expected isolated seed");
        setup.Add(new Firma{Id=3,FirmaAdi="Synthetic ID3"});
        setup.AddRange(new Kullanici{Id=103,FirmaId=3,KullaniciAdi="target"},
            new Kullanici{Id=102,FirmaId=74,KullaniciAdi="other"},
            new Kullanici{Id=104,FirmaId=74,Rol="SuperAdmin",KullaniciAdi="unmapped-admin"});
        setup.AddRange(new Fatura{Id=901,FirmaId=3,FaturaNo="target-one"},new Fatura{Id=902,FirmaId=3,FaturaNo="target-two"},
            new Fatura{Id=903,FirmaId=74,FaturaNo="preserved"});
        setup.AddRange(new IslemGecmisi{Id=1001,FirmaId=3,KullaniciId=101,Modul="Synthetic"},
            new IslemGecmisi{Id=1002,FirmaId=3,KullaniciId=103,Modul="Synthetic"},
            new IslemGecmisi{Id=1003,FirmaId=3,KullaniciId=null,Modul="Synthetic"},
            new IslemGecmisi{Id=1004,FirmaId=74,KullaniciId=102,Modul="Preserved"});
        setup.Add(new PrivateMessage{Id=501,SenderPerson=1,SenderName="Synthetic independent",Content="preserved",CreatedAtUtc=DateTime.UtcNow});
        await setup.SaveChangesAsync();
        var env=new Proof.FixtureEnvironment();Directory.CreateDirectory(env.WebRootPath);
        var outsideBytes="preserved unrelated bytes"u8.ToArray();
        var outsidePath=Path.Combine(env.WebRootPath,"unrelated-fixture");await File.WriteAllBytesAsync(outsidePath,outsideBytes);
        await AssertPreview(false);
        Console.WriteLine("PASS ID3 preview accepts mapped central administrator, local actor and existing nullable-actor semantics; scope remains 28/28");

        foreach(var actor in new[]{102,104,999999,0})
        {
            await SetActor(actor);
            await AssertPreview(true);
            var before=await Digest();
            await ExpectRejected(3);
            Check(await Digest()==before,"blocked actor leaves all rows unchanged");
        }
        await SetActor(101);
        Console.WriteLine("PASS preview/deletion both reject other-company normal user, unmapped SuperAdmin, missing actor and zero ID without partial deletion");

        await using(var owner=Context(owner:true))
        {
            await owner.Database.ExecuteSqlRawAsync("UPDATE public.firmova_company_deletion_owner SET user_id=104 WHERE singleton");
            await AssertPreview(true);await ExpectRejected(3);
            await owner.Database.ExecuteSqlRawAsync("UPDATE public.firmova_company_deletion_owner SET user_id=101 WHERE singleton");
            await owner.Database.ExecuteSqlRawAsync("""UPDATE public."Firmalar" SET "AktifMi"=false WHERE "Id"=72""");
            await AssertPreview(true);await ExpectRejected(3);
            await owner.Database.ExecuteSqlRawAsync("""UPDATE public."Firmalar" SET "AktifMi"=true WHERE "Id"=72""");
        }
        Console.WriteLine("PASS administrator exception requires current central mapping and active administrator company");

        // The exception must not spill over to other historical columns or incoming links.
        await using(var writer=Context())
        {
            writer.Add(new OdemePlani{Id=701,FirmaId=3,OlusturanKullaniciId=101});await writer.SaveChangesAsync();
            await ExpectRejected(3);
            await writer.Database.ExecuteSqlRawAsync("""DELETE FROM public."OdemePlanlari" WHERE "Id"=701""");
            await writer.Database.ExecuteSqlRawAsync("""UPDATE public."IslemGecmisleri" SET "KullaniciId"=103 WHERE "Id"=1004""");
            await using(var previewDb=Context(readOnly:true))
            {
                var preview=await new CompanyDeletionInventory(previewDb).InspectAsync(3,default);
                Check(preview.Tables.Single(x=>x.Name=="Kullanicilar").HasExternalReferences,"incoming other-company history still blocks");
            }
            await ExpectRejected(3);
            await writer.Database.ExecuteSqlRawAsync("""UPDATE public."IslemGecmisleri" SET "KullaniciId"=102 WHERE "Id"=1004""");
            writer.Add(new EkDosya{FirmaId=3,FaturaId=901,DosyaYolu="/uploads/faturalar/unknown.pdf"});await writer.SaveChangesAsync();
            await ExpectRejected(3);
            await writer.Database.ExecuteSqlRawAsync("""DELETE FROM public."EkDosyalar" WHERE "FirmaId"=3""");
        }
        await ExpectProtected(1);await ExpectProtected(72);
        Console.WriteLine("PASS payment creator, incoming references, unknown file ownership and protected companies retain their barriers");

        await AssertPreview(false);
        var preserved=await Digest(excludeTarget:true);
        await using(var deleting=Context())
            await new PostgreSqlCompanyDeletion(deleting).DeleteCompanyAsync(3,72,101,Guid.NewGuid(),new CompanyDocumentStore(deleting,env),default);
        Check(await Digest(excludeTarget:true)==preserved,"administrator, protected firms, other companies and all private tables unchanged");
        await using(var after=Context())
        {
            Check(!await after.Firmalar.AnyAsync(x=>x.Id==3) && !await after.Kullanicilar.AnyAsync(x=>x.FirmaId==3) &&
                !await after.Faturalar.AnyAsync(x=>x.FirmaId==3) && !await after.IslemGecmisleri.AnyAsync(x=>x.FirmaId==3),"target-owned rows removed");
            Check(await after.Kullanicilar.AnyAsync(x=>x.Id==101 && x.FirmaId==72) && await after.Firmalar.AnyAsync(x=>x.Id==1) &&
                await after.Firmalar.AnyAsync(x=>x.Id==72) && await after.Firmalar.AnyAsync(x=>x.Id==74),"administrator never included in target snapshot");
        }
        Check((await File.ReadAllBytesAsync(outsidePath)).SequenceEqual(outsideBytes),"unrelated bytes preserved");
        Console.WriteLine("PASS restricted-role synthetic ID3 deletion removes only its users/two invoices/audit rows; central administrator and private/other-company data preserved");
    }
    private static async Task AssertPreview(bool blocked)
    {
        await using var db=Context(readOnly:true);
        var result=await new CompanyDeletionInventory(db).InspectAsync(3,default);
        Check(result.Tables.Count==28 && result.Tables.Single(x=>x.Name=="IslemGecmisleri").Rows==3,"preview scope and counts");
        Check(result.Tables.Single(x=>x.Name=="IslemGecmisleri").HasExternalReferences==blocked,"preview uses same actor policy as deletion");
    }
    private static async Task SetActor(int actor)
    {await using var db=Context();await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE public."IslemGecmisleri" SET "KullaniciId"={actor} WHERE "Id"=1001""");}
    private static async Task ExpectRejected(int company)
    {
        await using var db=Context();
        try{await new PostgreSqlCompanyDeletion(db).DeleteCompanyAsync(company,72,101,Guid.NewGuid(),new CompanyDocumentStore(db,new Proof.FixtureEnvironment()),default);}
        catch(InvalidDataException){return;}
        throw new InvalidDataException("Unsafe fixture deletion was accepted.");
    }
    private static async Task ExpectProtected(int company)
    {
        await using var db=Context();
        try{await new PostgreSqlCompanyDeletion(db).DeleteCompanyAsync(company,72,101,Guid.NewGuid(),new CompanyDocumentStore(db,new Proof.FixtureEnvironment()),default);}
        catch(Exception ex) when(ex is ArgumentOutOfRangeException or InvalidOperationException){return;}
        throw new InvalidDataException("Protected company accepted.");
    }
    private static async Task<string> Digest(bool excludeTarget=false)
    {
        await using var db=Context(readOnly:true);
        return await db.Database.SqlQuery<string>($"""
            SELECT md5(string_agg(r::text,chr(10) ORDER BY r::text)) AS "Value" FROM (
              SELECT jsonb_build_array('company',to_jsonb(t)) r FROM public."Firmalar" t WHERE NOT {excludeTarget} OR t."Id"<>3
              UNION ALL SELECT jsonb_build_array('user',to_jsonb(t)) FROM public."Kullanicilar" t WHERE NOT {excludeTarget} OR t."FirmaId"<>3
              UNION ALL SELECT jsonb_build_array('invoice',to_jsonb(t)) FROM public."Faturalar" t WHERE NOT {excludeTarget} OR t."FirmaId"<>3
              UNION ALL SELECT jsonb_build_array('history',to_jsonb(t)) FROM public."IslemGecmisleri" t WHERE NOT {excludeTarget} OR t."FirmaId"<>3
              UNION ALL SELECT jsonb_build_array('private',to_jsonb(t)) FROM public."PrivateMessages" t
              UNION ALL SELECT jsonb_build_array('presence',to_jsonb(t)) FROM public."PrivatePresence" t
              UNION ALL SELECT jsonb_build_array('hidden',to_jsonb(t)) FROM public."PrivateMessageHidden" t
              UNION ALL SELECT jsonb_build_array('media',to_jsonb(t)) FROM public."PrivateMediaContents" t
              UNION ALL SELECT jsonb_build_array('quota',to_jsonb(t)) FROM public."PrivateMediaQuotas" t
            ) snapshot
            """).SingleAsync();
    }
    private static void Check(bool value,string message){if(!value)throw new InvalidDataException(message);}
}
