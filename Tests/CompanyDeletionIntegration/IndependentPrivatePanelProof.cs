using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Helpers;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;
using Npgsql;

internal static class IndependentPrivatePanelProof
{
    private const string Owner="Host=/work;Database=firmova_stage10_synthetic;Username=postgres;Pooling=false";
    private const string Runtime="Host=/work;Database=firmova_stage10_synthetic;Username=firmova_app_runtime;Pooling=false";
    private const string PrivateUser="synthetic-private";
    private const string PrivatePassword="SyntheticPrivateOnly!55";
    private const string ErpPassword="SyntheticErpOnly!73";
    private static AppDbContext Context()=>new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Runtime).Options);
    private static async Task Sql(NpgsqlConnection c,string text){await using var q=new NpgsqlCommand(text,c);await q.ExecuteNonQueryAsync();}
    internal static async Task RunAsync()
    {
        if(!OperatingSystem.IsLinux())throw new InvalidOperationException("Synthetic Linux fixture only.");
        await using var owner=new NpgsqlConnection(Owner);await owner.OpenAsync();
        // Install only reviewed private trigger definitions, not their migration/history/table DDL.
        var reviewed=await File.ReadAllTextAsync("/private-media-schema.sql");
        foreach(Match m in Regex.Matches(reviewed,@"(?s)CREATE FUNCTION public\.firmova_private_media_[a-z_]+\(\).*?\$body\$;|CREATE TRIGGER firmova_private_media_[a-z_]+.*?;"))
            await Sql(owner,m.Value);
        await using var db=Context();
        var administrator=await db.Kullanicilar.SingleAsync(x=>x.Id==101);
        administrator.KullaniciAdi="synthetic-admin";administrator.Sifre=PasswordHelper.Hash(administrator,"SyntheticAdminOnly!72");
        var victim=new Kullanici{Id=103,FirmaId=73,KullaniciAdi="synthetic-victim"};victim.Sifre=PasswordHelper.Hash(victim,ErpPassword);
        db.Add(victim);db.AddRange(new Fatura{Id=901,FirmaId=73,FaturaNo="target"},new Fatura{Id=902,FirmaId=74,FaturaNo="preserve"});
        var now=new DateTime(DateTime.UtcNow.Ticks/TimeSpan.TicksPerMillisecond*TimeSpan.TicksPerMillisecond,DateTimeKind.Utc);
        var mediaKey=Guid.NewGuid().ToString("N");
        db.AddRange(new PrivateMessage{Id=501,SenderPerson=1,SenderName="Synthetic global one",Content="private fixture",CreatedAtUtc=now},
            new PrivateMessage{Id=502,SenderPerson=2,SenderName="Synthetic global two",Content="",Kind=PrivateMessageKind.Audio,MediaKey=mediaKey,CreatedAtUtc=now});
        db.AddRange(new PrivatePresence{PersonNumber=1,LastSeenAtUtc=now},new PrivatePresence{PersonNumber=2,LastSeenAtUtc=now});
        await db.SaveChangesAsync();
        db.Add(new PrivateMessageHidden{PersonNumber=2,PrivateMessageId=501,HiddenAtUtc=now});
        db.Add(new PrivateMediaContent{MessageId=502,MediaKey=mediaKey,Payload="synthetic private payload"u8.ToArray(),CreatedAtUtc=now,ExpiresAtUtc=now.AddHours(24)});
        await db.SaveChangesAsync();
        Check(await IndependentPrivatePanelScope.IsCompatibleAsync(db,default),"reviewed independent schema accepted including quota triggers");

        Directory.CreateDirectory("/recovery/wwwroot");Directory.CreateDirectory("/recovery/App_Data/PrivateMedia");
        var privateFile="/recovery/App_Data/PrivateMedia/preserved-fixture";await File.WriteAllTextAsync(privateFile,"independent private bytes");
        var keyDirectory="/recovery/wwwroot/uploads/.firmova-private/data-protection";Directory.CreateDirectory(keyDirectory);
        var dp=DataProtectionProvider.Create(new DirectoryInfo(keyDirectory));dp.CreateProtector("synthetic seed").Protect("synthetic");
        File.SetUnixFileMode(keyDirectory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        foreach(var keyFile in Directory.GetFiles(keyDirectory))File.SetUnixFileMode(keyFile,UnixFileMode.UserRead|UnixFileMode.UserWrite);
        using var rsa=RSA.Create(2048);
        var certificateRequest=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var san=new SubjectAlternativeNameBuilder();san.AddIpAddress(IPAddress.Loopback);san.AddDnsName("localhost");certificateRequest.CertificateExtensions.Add(san.Build());
        using var certificate=certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddHours(1));
        var pfxPassword=Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await File.WriteAllBytesAsync("/work/synthetic-server.pfx",certificate.Export(X509ContentType.Pfx,pfxPassword));
        var pin=certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var start=new ProcessStartInfo("/runtime/dotnet","/application/MuhasebeTakip2.App.dll")
        {UseShellExecute=false,WorkingDirectory="/recovery",RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var pair in new Dictionary<string,string>{
            ["ASPNETCORE_ENVIRONMENT"]="Testing",["ASPNETCORE_URLS"]="https://127.0.0.1:5443",["ASPNETCORE_WEBROOT"]="/recovery/wwwroot",
            ["ASPNETCORE_Kestrel__Certificates__Default__Path"]="/work/synthetic-server.pfx",["ASPNETCORE_Kestrel__Certificates__Default__Password"]=pfxPassword,
            ["ConnectionStrings__Default"]=Runtime,["Database__Provider"]="PostgreSql",["Database__AutoMigrate"]="false",
            ["Maintenance__AttachLegacyDataToFirstFirm"]="false",["Maintenance__FixDefaultAdminOnStartup"]="false",
            ["CompanyDeletion__Enabled"]="true",["CompanyDocuments__CleanupEnabled"]="false",["CompanyDocuments__WritePrivate"]="true",
            ["CompanyRecovery__Enabled"]="true",["DataProtection__PersistKeys"]="true",["AdminPortal__MainAdminUserId"]="101",
            ["FIRMOVA_PRIVATE_USERNAME"]=PrivateUser,["FIRMOVA_PRIVATE_PERSON_1"]="Synthetic global one",["FIRMOVA_PRIVATE_PERSON_2"]="Synthetic global two",
            ["FIRMOVA_PRIVATE_PASSWORD_HASH_1"]=new PasswordHasher<PrivatePerson>().HashPassword(new("1","Synthetic global one"),PrivatePassword),
            ["FIRMOVA_PRIVATE_PASSWORD_HASH_2"]=new PasswordHasher<PrivatePerson>().HashPassword(new("2","Synthetic global two"),"SyntheticOtherPrivate!56")})start.Environment[pair.Key]=pair.Value;
        using var app=Process.Start(start)!;
        var stdout=app.StandardOutput.ReadToEndAsync();var stderr=app.StandardError.ReadToEndAsync();
        HttpClient Client()=>new(new HttpClientHandler{AllowAutoRedirect=false,ServerCertificateCustomValidationCallback=(_,cert,_,_)=>
            cert is not null && cert.GetCertHashString(HashAlgorithmName.SHA256)==pin}){BaseAddress=new Uri("https://127.0.0.1:5443"),Timeout=TimeSpan.FromSeconds(25)};
        using var administratorClient=Client();using var victimClient=Client();using var privateClient=Client();
        try
        {
            var ready=false;
            for(var i=0;i<100;i++)
            {
                if(app.HasExited)throw new InvalidDataException("Synthetic real application exited before health.");
                try{using var health=await administratorClient.GetAsync("/health/live");if(health.IsSuccessStatusCode){ready=true;break;}}catch(HttpRequestException){}
                await Task.Delay(100);
            }
            Check(ready,"real application started over pinned synthetic HTTPS");
            await Login(administratorClient,"synthetic-admin","SyntheticAdminOnly!72");
            await Login(victimClient,"synthetic-victim",ErpPassword);
            // Switching from an ERP administrator into the private realm must discard ERP authority.
            await Login(privateClient,"synthetic-admin","SyntheticAdminOnly!72");
            await Login(privateClient,PrivateUser,PrivatePassword);
            await DenyErpFromPrivate(privateClient);
            using(var panel=await privateClient.GetAsync("/Panel?handler=Messages"))Check(panel.IsSuccessStatusCode,"separate private authorization works");
            Console.WriteLine("PASS real HTTPS login: private renewal drops ERP authority; portal, accounting, recovery and APIs deny private session");
            var snapshot=await PrivateSnapshot(owner);
            var fileBytes=await File.ReadAllBytesAsync(privateFile);
            var (token,confirmation)=await Preview(administratorClient);

            await Sql(owner,"""ALTER TABLE public."PrivateMessages" ADD COLUMN "FirmaId" integer""");
            Check(!await IndependentPrivatePanelScope.IsCompatibleAsync(db,default),"new private company identity rejected");
            Check(await Delete(administratorClient,73,token,confirmation)==409,"coupled column blocks deletion");
            await Sql(owner,"ALTER TABLE public.\"PrivateMessages\" DROP COLUMN \"FirmaId\"");
            await Sql(owner,"""ALTER TABLE public."PrivatePresence" ADD CONSTRAINT synthetic_private_erp FOREIGN KEY("PersonNumber") REFERENCES public."Kullanicilar"("Id") NOT VALID""");
            Check(!await IndependentPrivatePanelScope.IsCompatibleAsync(db,default),"cross-domain FK rejected even if not validated");
            Check(await Delete(administratorClient,73,token,confirmation)==409,"cross-domain FK blocks deletion");
            await Sql(owner,"""ALTER TABLE public."PrivatePresence" DROP CONSTRAINT synthetic_private_erp""");
            await Sql(owner,"""
                CREATE FUNCTION public.synthetic_private_side_effect() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN UPDATE public."PrivateMediaQuotas" SET "UsedBytes"=0; RETURN OLD; END; $$;
                CREATE TRIGGER synthetic_side_effect BEFORE DELETE ON public."Faturalar" FOR EACH ROW EXECUTE FUNCTION public.synthetic_private_side_effect();
                """);
            Check(await Delete(administratorClient,73,token,confirmation)==409,"unreviewed cross-domain trigger blocks deletion");
            await Sql(owner,"""DROP TRIGGER synthetic_side_effect ON public."Faturalar"; DROP FUNCTION public.synthetic_private_side_effect()""");
            await Sql(owner,"""INSERT INTO public."EkDosyalar"("FirmaId","FaturaId","DosyaAdi","DosyaYolu","IcerikTipi","Boyut","YuklemeTarihi") VALUES(73,901,'unknown','/uploads/legacy-unknown.pdf','application/pdf',1,now())""");
            Check(await Delete(administratorClient,73,token,confirmation)==409,"uncertain accounting file still blocks deletion");
            await Sql(owner,"""DELETE FROM public."EkDosyalar" WHERE "FirmaId"=73""");
            Check(snapshot==await PrivateSnapshot(owner),"blocked operations preserved every private value and payload");
            Check(await Delete(administratorClient,1,token,confirmation)==403 && await Delete(administratorClient,72,token,confirmation)==403,"protected companies still blocked");
            Console.WriteLine("PASS schema/FK/trigger coupling fails closed; protected firms and unknown accounting files retain existing barriers");
            (token,confirmation)=await Preview(administratorClient);
            // Strong no-touch proof: runtime cannot SELECT, LOCK or mutate any private table.
            await Sql(owner,"""REVOKE ALL ON public."PrivateMessages",public."PrivatePresence",public."PrivateMessageHidden",public."PrivateMediaContents",public."PrivateMediaQuotas" FROM firmova_app_runtime""");
            await Sql(owner,"""BEGIN; UPDATE public."PrivatePresence" SET "ShowLastSeen"="ShowLastSeen" WHERE "PersonNumber"=1""");
            try{Check(await Delete(administratorClient,73,token,confirmation)==302,"company deletion succeeds without private permissions while private writer holds lock");}
            finally{await Sql(owner,"ROLLBACK");}
            Check(snapshot==await PrivateSnapshot(owner),"company deletion left all five private tables byte-equivalent");
            var afterBytes=await File.ReadAllBytesAsync(privateFile);Check(fileBytes.SequenceEqual(afterBytes),"private legacy file preserved");
            Check(!await db.Firmalar.AnyAsync(x=>x.Id==73) && !await db.Kullanicilar.AnyAsync(x=>x.Id==103) &&
                await db.Firmalar.CountAsync()==3 && await db.Faturalar.AnyAsync(x=>x.Id==902 && x.FirmaId==74),"only selected synthetic company removed");
            await Sql(owner,"""
                GRANT SELECT,INSERT,UPDATE,DELETE ON public."PrivateMessages",public."PrivatePresence",public."PrivateMessageHidden" TO firmova_app_runtime;
                GRANT SELECT,INSERT,DELETE ON public."PrivateMediaContents" TO firmova_app_runtime;
                GRANT SELECT("Id","UsedBytes"),UPDATE("UsedBytes") ON public."PrivateMediaQuotas" TO firmova_app_runtime;
                """);
            Console.WriteLine("PASS company deletion cannot read/lock/change private tables; messages, hidden/presence rows, media payload, quota and file preserved");
            using(var stale=await victimClient.GetAsync("/CariKartlar"))Check(stale.StatusCode==HttpStatusCode.Redirect,"deleted ERP session rejected");
            var missing=await Login(victimClient,"synthetic-victim",ErpPassword,expectSuccess:false);
            Check(WebUtility.HtmlDecode(missing).Contains("Bu hesap kayıtlı değil."),"deleted ERP login shows exact missing-account message");
            await Login(victimClient,PrivateUser,ErpPassword,expectSuccess:false);
            using(var denied=await victimClient.GetAsync("/Panel?handler=Messages"))Check(denied.StatusCode==HttpStatusCode.Unauthorized,"ERP credential does not authenticate private realm");
            await DenyErpFromPrivate(privateClient);
            using(var legitimate=await privateClient.GetAsync("/Panel?handler=Messages"))Check(legitimate.IsSuccessStatusCode,"independently granted private access remains valid");
            Console.WriteLine("PASS deleted ERP identity cannot use private realm to regain ERP/portal/API access; separate private grant intentionally remains valid");
        }
        finally
        {
            if(!app.HasExited){app.Kill(entireProcessTree:true);await app.WaitForExitAsync();}
            _=await stdout;_=await stderr; // Synthetic logs are never printed or committed.
        }
    }
    private static async Task<string> Login(HttpClient client,string name,string password,bool expectSuccess=true)
    {
        var html=await client.GetStringAsync("/Login");
        using var response=await client.PostAsync("/Login",new FormUrlEncodedContent(new Dictionary<string,string>{["KullaniciAdi"]=name,["Sifre"]=password,["__RequestVerificationToken"]=Field(html,"__RequestVerificationToken")}));
        if(expectSuccess)Check(response.StatusCode==HttpStatusCode.Redirect,"synthetic login expected redirect");
        return await response.Content.ReadAsStringAsync();
    }
    private static async Task DenyErpFromPrivate(HttpClient client)
    {
        foreach(var path in new[]{"/CariKartlar","/Araclar/Yedekleme","/SuperAdmin/FirmaSilmeOnizleme?id=74","/AdminPortal"})
        {using var r=await client.GetAsync(path);Check(r.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden,"private identity denied ERP management path");}
        using var api=await client.PostAsync("/api/ai/calisan-avans-toplam",new StringContent("{}",System.Text.Encoding.UTF8,"application/json"));
        Check(api.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized,"private identity denied ERP API");
    }
    private static async Task<(string Token,string Confirmation)> Preview(HttpClient client)
    {
        var html=await client.GetStringAsync("/SuperAdmin/FirmaSilmeOnizleme?id=73");
        var token=Field(html,"__RequestVerificationToken");var confirmation=Field(html,"confirmation");
        Check(token.Length>0 && confirmation.Length>0 && html.Contains("Firma dışında korunan özel panel"),"eligible preview explicitly preserves independent private panel");
        return(token,confirmation);
    }
    private static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+name+"\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task<int> Delete(HttpClient client,int company,string token,string confirmation)
    {using var r=await client.PostAsync("/SuperAdmin/FirmaSilmeOnizleme",new FormUrlEncodedContent(new Dictionary<string,string>{["id"]=company.ToString(),["confirmation"]=confirmation,["__RequestVerificationToken"]=token}));return(int)r.StatusCode;}
    private static async Task<string> PrivateSnapshot(NpgsqlConnection owner)
    {
        await using var c=new NpgsqlCommand("""
            SELECT md5(string_agg(r::text,chr(10) ORDER BY r::text)) FROM(
              SELECT jsonb_build_array('messages',to_jsonb(t)) r FROM public."PrivateMessages" t
              UNION ALL SELECT jsonb_build_array('presence',to_jsonb(t)) FROM public."PrivatePresence" t
              UNION ALL SELECT jsonb_build_array('hidden',to_jsonb(t)) FROM public."PrivateMessageHidden" t
              UNION ALL SELECT jsonb_build_array('media',to_jsonb(t)) FROM public."PrivateMediaContents" t
              UNION ALL SELECT jsonb_build_array('quota',to_jsonb(t)) FROM public."PrivateMediaQuotas" t
            ) s
            """,owner);return(string)(await c.ExecuteScalarAsync())!;
    }
    private static void Check(bool ok,string name){if(!ok)throw new InvalidDataException(name);}
}
