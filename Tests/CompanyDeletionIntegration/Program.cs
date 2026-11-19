using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;
if(!OperatingSystem.IsLinux() || args is not ["schema" or "run" or "locks" or "package" or "compat" or "preview" or "private-scope" or "audit-actor"])throw new InvalidOperationException("Synthetic Linux fixture only.");
if(args[0]=="schema")
{
    await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=/work;Database=firmova_stage10_synthetic;Username=postgres").Options);
    await db.Database.EnsureCreatedAsync();
    await db.Database.ExecuteSqlRawAsync("CREATE TABLE public.\"CalisanArsivleri\" (\"Id\" integer PRIMARY KEY, \"FirmaId\" integer NOT NULL, \"EskiCalisanId\" integer NOT NULL, \"AdSoyad\" text NOT NULL, \"Telefon\" text NOT NULL, \"Maas\" numeric NOT NULL, \"IseGirisTarihi\" timestamp with time zone NOT NULL, \"AyrilisTarihi\" timestamp with time zone NOT NULL, \"AyrilisNotu\" text NOT NULL)");
    return;
}
if(args[0]=="locks") { await DeletionLockProof.RunAsync(); return; }
if(args[0]=="compat") { await PackageRecoveryCompatibility.RunAsync(); return; }
if(args[0]=="preview") { await ArchivePreviewProof.RunAsync(); return; }
if(args[0]=="private-scope") { await IndependentPrivatePanelProof.RunAsync(); return; }
if(args[0]=="audit-actor") { await CompanyAuditActorProof.RunAsync(); return; }
await Proof.RunAsync("firmova_stage10_synthetic", args[0]=="package");
if(args[0]=="package") await PackageRecoveryCompatibility.RunAsync();
internal static class Proof
{
    internal static async Task RunAsync(string database, bool installed = false)
    {
        if (database != "firmova_stage10_synthetic")
            throw new ArgumentException("Expected synthetic fixture.");
        var connection = $"Host=/work;Database={database};Username=postgres;SSL Mode=Disable;Pooling=false";
        var runtimeConnection = new Npgsql.NpgsqlConnectionStringBuilder(connection) { Username = "firmova_app_runtime" }.ConnectionString;
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options);
        if (!installed)
        {
            if (await db.Firmalar.AnyAsync()) throw new InvalidDataException("Expected empty fixture.");
            foreach (var id in new[] { 1, 72, 73, 74 }) db.Add(new Firma { Id = id, FirmaAdi = $"Synthetic {id}" });
            db.Add(new Kullanici { Id = 101, FirmaId = 72, Rol = "SuperAdmin", KullaniciAdi = "synthetic-owner" });
        }
        else if (await db.Firmalar.CountAsync()!=4 || await db.Kullanicilar.CountAsync()!=1 ||
                 !await db.Kullanicilar.AnyAsync(x=>x.Id==101 && x.FirmaId==72 && x.Rol=="SuperAdmin"))
            throw new InvalidDataException("Expected reviewed synthetic package setup.");
        db.AddRange(new Kullanici { Id = 102, FirmaId = 74, Rol = "SuperAdmin", KullaniciAdi = "synthetic-other" },
            new Kullanici { Id = 103, FirmaId = 73, KullaniciAdi = "synthetic-deleted" });
        await db.SaveChangesAsync();
        if (!installed)
        {
        var guard=await File.ReadAllTextAsync("/tests/primary-company-guard-fixture.sql");
        guard=string.Join("\n",guard.Split('\n').Where(line=>!line.TrimStart().StartsWith("\\"))).Replace(":'main_admin_user_id'","101");
        await db.Database.ExecuteSqlRawAsync(guard);
        var body=await File.ReadAllTextAsync("/sql/CompanyDocumentFileLocks.sql");
        var fixtureInstallation="GRANT SELECT (singleton,user_id) ON public.firmova_company_deletion_owner TO firmova_app_runtime; GRANT DELETE ON public.\"Firmalar\" TO firmova_app_runtime;"+body;
        await db.Database.ExecuteSqlRawAsync(fixtureInstallation.Replace("{","{{").Replace("}","}}"));
        }
        await using(var runtimeConnectionCheck = new Npgsql.NpgsqlConnection(runtimeConnection))
        {
            await runtimeConnectionCheck.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand("SELECT session_user=current_user AND current_user='firmova_app_runtime' AND NOT has_table_privilege(current_user,'public.\"CompanyStoredFileReferences\"','DELETE')",runtimeConnectionCheck);
            if(!Equals(await command.ExecuteScalarAsync(),true)) throw new InvalidDataException("Expected direct least-privilege runtime login.");
        }
        db.Faturalar.AddRange(new Fatura { FirmaId = 73, FaturaNo = "delete-fixture", AktifMi = true },
            new Fatura { FirmaId = 74, FaturaNo = "preserve-fixture", AktifMi = true });
        await db.SaveChangesAsync();
        var store = new CompanyDocumentStore(db, new FixtureEnvironment());
        db.AddRange(new KasaHesap{Id=500,FirmaId=73,Ad="Synthetic source"},new KasaHesap{Id=501,FirmaId=73,Ad="Synthetic destination"},new KasaHesap{Id=502,FirmaId=74,Ad="Preserve"});
        await db.SaveChangesAsync();
        db.Add(new KasaTransfer{FirmaId=73,KaynakHesapId=500,HedefHesapId=501,Tutar=10});
        db.Add(new KasaHareket{FirmaId=73,KasaHesapId=500,Tarih=DateTime.UtcNow,Tutar=5});
        await db.SaveChangesAsync();
        using var content = new MemoryStream("synthetic attachment"u8.ToArray());
        var upload = new FormFile(content, 0, content.Length, "file", "fixture.pdf");
        var invoiceId = await db.Faturalar.Where(x => x.FirmaId == 73).Select(x => x.Id).SingleAsync();
        var env=new FixtureEnvironment();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["CompanyDocuments:WritePrivate"]="true"}).Build();
        var published=AccountingDocuments.PrepareUpload(env,config,"faturalar",$"fatura-{invoiceId}-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(published.PhysicalPath,"synthetic attachment"u8.ToArray());
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            var attachment=new EkDosya{FirmaId=73,FaturaId=invoiceId,DosyaYolu=published.StoredPath};db.Add(attachment);await db.SaveChangesAsync();
            await store.RegisterPublishedAsync(published.StoredPath,attachment,null,default);await tx.CommitAsync();
        }
        var storedFile = await db.Set<CompanyStoredFile>().Select(x => x.Id).SingleAsync();
        var originalPhysicalPath=store.ObjectPath(storedFile);
        var otherInvoice=await db.Faturalar.Where(x=>x.FirmaId==74).Select(x=>x.Id).SingleAsync();
        var other=AccountingDocuments.PrepareUpload(env,config,"faturalar",$"fatura-{otherInvoice}-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(other.PhysicalPath,"preserve other company bytes");
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            var own=new EkDosya{FirmaId=74,FaturaId=otherInvoice,DosyaYolu=other.StoredPath};db.Add(own);await db.SaveChangesAsync();
            await store.RegisterPublishedAsync(other.StoredPath,own,null,default);await tx.CommitAsync();
        }
        try
        {
            await new PostgreSqlCompanyDeletion(db).DeleteCompanyAsync(73, 72, 101, Guid.NewGuid(), store, default);
            throw new InvalidOperationException("The PostgreSQL owner was allowed to delete a company.");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("database role", StringComparison.Ordinal)) { }
        if (!await db.Firmalar.AnyAsync(x => x.Id == 73))
            throw new InvalidDataException("Owner rejection changed the company.");
        Directory.CreateDirectory("/recovery/wwwroot");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ContentRootPath = "/recovery", WebRootPath = "/recovery/wwwroot" });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["AdminPortal:MainAdminUserId"] = "101";
        builder.Configuration["CompanyDeletion:Enabled"] = "true";
        builder.Configuration["CompanyDocuments:CleanupEnabled"] = "true";
        builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(runtimeConnection));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAccountingContextFoundation();
        builder.Services.AddAdminPortalAuthorization();
        builder.Services.AddScoped<CompanyDocumentStore>(); // Drain explicitly to test restart states deterministically.
        builder.Services.AddScoped<PrivateAccessService>();
        builder.Services.AddSingleton<PrivateLoginAttemptLimiter>();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession();
        builder.Services.AddRazorPages().AddApplicationPart(typeof(AppDbContext).Assembly);
        builder.Services.AddScoped<IOdemeBildirimService, OdemeBildirimService>();
        await using var app = builder.Build();
        app.UseSession();
        app.Use(CompanySessionValidation.InvokeAsync);
        app.MapGet("/fixture-session-check", () => Results.NoContent());
        app.MapRazorPages();
        app.MapPost("/fixture-login/{user:int}", async (int user, HttpContext context, AccountingSessionLifecycle lifecycle) =>
        {
            await lifecycle.ReplaceAsync(context, () => {
                context.Session.SetInt32("KullaniciId", user);
                context.Session.SetInt32("FirmaId", user == 101 ? 72 : user == 103 ? 73 : 74);
                context.Session.SetString("Rol", "SuperAdmin");
                return Task.CompletedTask;
            }, establishAccounting: true);
            return Results.NoContent();
        });
        await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(app.Urls.Single()) };
        using var victim = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { BaseAddress = new Uri(app.Urls.Single()) };
        (await victim.PostAsync("/fixture-login/103", null)).EnsureSuccessStatusCode();
        (await victim.GetAsync("/fixture-session-check")).EnsureSuccessStatusCode();
        const string path = "/SuperAdmin/FirmaSilmeOnizleme";
        (await client.PostAsync("/fixture-login/102", null)).EnsureSuccessStatusCode();
        var unauthorizedList=await client.GetStringAsync("/SuperAdmin/Index");
        if(unauthorizedList.Contains("FirmaSilmeOnizleme",StringComparison.Ordinal))
            throw new InvalidDataException("Deletion button visible to other administrator.");
        if ((int)(await client.GetAsync(path + "?id=73")).StatusCode != 403)
            throw new InvalidDataException("Unauthorized preview accepted.");
        (await client.PostAsync("/fixture-login/101", null)).EnsureSuccessStatusCode();
        var authorizedList=await client.GetStringAsync("/SuperAdmin/Index");
        var visibleTargets=Regex.Matches(authorizedList,@"FirmaSilmeOnizleme\?id=([0-9]+)").Select(m=>int.Parse(m.Groups[1].Value)).ToArray();
        if(!visibleTargets.Contains(73) || visibleTargets.Contains(1) || visibleTargets.Contains(72))
            throw new InvalidDataException("Protected company button visibility failed.");
        var html = await client.GetStringAsync(path + "?id=73");
        string Field(string field) => System.Net.WebUtility.HtmlDecode(Regex.Match(html,
            $"name=\"{field}\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var token = Field("__RequestVerificationToken");
        var confirmation = Field("confirmation");
        if (token.Length == 0 || confirmation.Length == 0)
            throw new InvalidDataException("Missing bare deletion confirmation.");
        async Task<int> Post(int target, bool csrf = true) => (int)(await client.PostAsync(path, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["id"] = target.ToString(), ["confirmation"] = confirmation,
                ["__RequestVerificationToken"] = csrf ? token : "" }))).StatusCode;
        var flags=app.Services.GetRequiredService<IConfiguration>();
        flags["CompanyDeletion:Enabled"]="false";
        if(await Post(73)!=409 || !await db.Firmalar.AnyAsync(x=>x.Id==73))throw new InvalidDataException("Disabled deletion changed records.");
        flags["CompanyDeletion:Enabled"]="true";
        flags["CompanyDocuments:CleanupEnabled"]="false";
        if(await Post(73)!=409 || !await db.Firmalar.AnyAsync(x=>x.Id==73))throw new InvalidDataException("Missing cleanup worker did not block.");
        flags["CompanyDocuments:CleanupEnabled"]="true";
        if (await Post(73, false) != 400 || await Post(1) != 403 || await Post(72) != 403 || await Post(74) != 400)
            throw new InvalidDataException("HTTP guard rejected the wrong requests.");
        (await client.PostAsync("/fixture-login/102", null)).EnsureSuccessStatusCode();
        if (await Post(73) != 403) throw new InvalidDataException("Unauthorized POST accepted.");
        (await client.PostAsync("/fixture-login/101", null)).EnsureSuccessStatusCode();
        if(await Post(73)!=400)throw new InvalidDataException("Confirmation survived login epoch replacement.");
        html=await client.GetStringAsync(path+"?id=73");token=Field("__RequestVerificationToken");confirmation=Field("confirmation");
        db.Add(new PrivateMessage { SenderPerson = 1, Content = "Synthetic independent panel" });
        await db.SaveChangesAsync();
        db.Add(new PrivatePresence{PersonNumber=1,LastSeenAtUtc=DateTime.UtcNow});await db.SaveChangesAsync();
        var privatePreview=await client.GetStringAsync(path+"?id=73");
        if(!privatePreview.Contains("name=\"confirmation\"",StringComparison.Ordinal))
            throw new InvalidDataException("Independent panel unexpectedly blocked preview.");
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE public."PrivateMessages" ADD COLUMN "FirmaId" integer""");
        if((await client.GetStringAsync(path+"?id=73")).Contains("name=\"confirmation\"",StringComparison.Ordinal) || await Post(73)!=409)
            throw new InvalidDataException("Unexpected private/company coupling did not fail closed.");
        await db.Database.ExecuteSqlRawAsync("""ALTER TABLE public."PrivateMessages" DROP COLUMN "FirmaId" """);
        await db.Database.ExecuteSqlRawAsync("""CREATE TABLE public."SyntheticUnmappedCompanyRows" ("FirmaId" integer NOT NULL)""");
        if (await Post(73) != 409 || !await db.Firmalar.AnyAsync(x => x.Id == 73))
            throw new InvalidDataException("Unmapped company table did not block deletion.");
        await db.Database.ExecuteSqlRawAsync("""DROP TABLE public."SyntheticUnmappedCompanyRows" """);
        var legacy = new EkDosya { FirmaId = 73, FaturaId = invoiceId, DosyaYolu = "/uploads/legacy-fixture.pdf" };
        db.Add(legacy);
        await db.SaveChangesAsync();
        if (await Post(73) != 409 || !await db.EkDosyalar.AnyAsync(x => x.Id == legacy.Id))
            throw new InvalidDataException("Unverified attachment did not block deletion.");
        db.Remove(legacy);
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(originalPhysicalPath,"synthetic changed bytes");
        if(await Post(73)!=409 || !await db.Firmalar.AnyAsync(x=>x.Id==73))
            throw new InvalidDataException("Changed bytes were accepted for deletion.");
        await File.WriteAllTextAsync(originalPhysicalPath,"synthetic attachment");
        var shared=new EkDosya{FirmaId=74,FaturaId=otherInvoice,DosyaYolu=published.StoredPath.Replace(".pdf","%2epdf")};
        db.Add(shared);await db.SaveChangesAsync();
        if(await Post(73)!=409 || !File.Exists(originalPhysicalPath) || !await db.Firmalar.AnyAsync(x=>x.Id==73))
            throw new InvalidDataException("Shared encoded live path was not preserved.");
        db.Remove(shared);await db.SaveChangesAsync();
        var confirmedStatus = await Post(73);
        var replayStatus = await Post(73);
        if (confirmedStatus != 302 || replayStatus != 409 ||
            await db.Firmalar.AnyAsync(x => x.Id == 73) || await db.Firmalar.CountAsync() != 3 ||
            await db.Kullanicilar.AnyAsync(x => x.Id == 103) || await db.Faturalar.AnyAsync(x => x.FirmaId == 73) ||
            !await db.Faturalar.AnyAsync(x => x.FirmaId == 74 && x.FaturaNo == "preserve-fixture"))
            throw new InvalidDataException($"Confirmed deletion or replay failed: {confirmedStatus}, {replayStatus}.");
        if(await db.PrivateMessages.CountAsync()!=1 || await db.PrivatePresences.CountAsync()!=1)
            throw new InvalidDataException("Independent private panel changed during company deletion.");
        if (!File.Exists(store.ObjectPath(storedFile)) ||
            !await db.Set<CompanyFileCleanupJob>().AnyAsync(x => x.FileId == storedFile && x.CompanyId == 73))
            throw new InvalidDataException("Verified document was not queued safely.");
        if(await db.KasaHesaplar.AnyAsync(x=>x.FirmaId==73) || await db.KasaTransferler.AnyAsync(x=>x.FirmaId==73) || !await db.KasaHesaplar.AnyAsync(x=>x.FirmaId==74))
            throw new InvalidDataException("Composite cash ownership changed outside target.");
        var laterReference=new EkDosya{FirmaId=74,FaturaId=otherInvoice,DosyaYolu=published.StoredPath.Replace(".pdf","%2epdf")};
        db.Add(laterReference);
        try { await db.SaveChangesAsync(); throw new InvalidDataException("Retired file accepted a new encoded reference."); }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23514" })
        { db.Entry(laterReference).State=EntityState.Detached; }
        if(!File.Exists(originalPhysicalPath) || await db.Set<CompanyFileCleanupJob>().AnyAsync(x=>x.FileId==storedFile && x.CompletedAtUtc!=null))
            throw new InvalidDataException("Rejected reference changed pending file state.");
        var independentId=Guid.NewGuid();
        var independentPath=Path.Combine(store.Objects,independentId.ToString("N"));
        var independentBytes="independent synthetic cleanup"u8.ToArray();
        await File.WriteAllBytesAsync(independentPath,independentBytes);
        db.Add(new CompanyStoredFile{Id=independentId,OwnerCompanyId=74,RetiredAtUtc=DateTime.UtcNow,
            Sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(independentBytes)),Length=independentBytes.Length});
        db.Add(new CompanyFileCleanupJob{FileId=independentId,CompanyId=74,OperationId=Guid.NewGuid(),EnqueuedAtUtc=DateTime.UtcNow});
        await db.SaveChangesAsync();
        await using(var busyDb=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(runtimeConnection).Options))
        await using(var busyTx=await busyDb.Database.BeginTransactionAsync())
        {
            await new CompanyDocumentStore(busyDb,new FixtureEnvironment()).LockObjectAsync(storedFile,default);
            await using var drainDb=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(runtimeConnection).Options);
            await CompanyFileCleanupWorker.DrainAsync(drainDb,new CompanyDocumentStore(drainDb,new FixtureEnvironment()),default);
            if(!File.Exists(originalPhysicalPath) || File.Exists(independentPath) ||
               !await db.Set<CompanyFileCleanupJob>().AnyAsync(x=>x.FileId==independentId && x.CompletedAtUtc!=null))
                throw new InvalidDataException("A busy company file held up independent-company cleanup.");
            await busyTx.RollbackAsync();
        }
        Console.WriteLine("PASS worker skips a busy file and completes another company's independent file");
        var pendingJob=await db.Set<CompanyFileCleanupJob>().AsNoTracking().SingleAsync(x=>x.FileId==storedFile);
        CompanyDiskStorage.EnsurePrivateDirectory(store.Quarantine);
        var quarantined=Path.Combine(store.Quarantine,$"{pendingJob.OperationId:N}-{storedFile:N}");
        File.Move(originalPhysicalPath,quarantined); // Synthetic interruption immediately after worker's quarantine move.
        await using(var cleanupDb=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(runtimeConnection).Options))
        {
            await CompanyFileCleanupWorker.DrainAsync(cleanupDb,new CompanyDocumentStore(cleanupDb,new FixtureEnvironment()),default);
            await CompanyFileCleanupWorker.DrainAsync(cleanupDb,new CompanyDocumentStore(cleanupDb,new FixtureEnvironment()),default);
        }
        if(File.Exists(originalPhysicalPath) || File.Exists(quarantined) || !await db.Set<CompanyFileCleanupJob>().AnyAsync(x=>x.FileId==storedFile && x.CompletedAtUtc!=null))
            throw new InvalidDataException("Persistent live-path cleanup did not complete safely.");
        Console.WriteLine("PASS current private path, composite cash keys and resumable cleanup integration");
        if(!File.Exists(other.PhysicalPath) || await File.ReadAllTextAsync(other.PhysicalPath)!="preserve other company bytes")
            throw new InvalidDataException("Other company file changed.");
        Console.WriteLine("PASS shared encoded paths block deletion and cleanup; other-company bytes preserved");
        var stale = await victim.GetAsync("/fixture-session-check");
        if ((int)stale.StatusCode != 302 || stale.Headers.Location?.OriginalString != "/Login")
            throw new InvalidDataException("Deleted account still has an ERP session.");
        var login = await victim.GetStringAsync("/Login");
        var loginToken = System.Net.WebUtility.HtmlDecode(Regex.Match(login,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        using var loginResponse = await victim.PostAsync("/Login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["KullaniciAdi"] = "synthetic-deleted", ["Sifre"] = "fixture", ["__RequestVerificationToken"] = loginToken }));
        if (!System.Net.WebUtility.HtmlDecode(await loginResponse.Content.ReadAsStringAsync()).Contains("Bu hesap kayıtlı değil."))
            throw new InvalidDataException("Missing-account login message not shown.");
        Console.WriteLine("PASS: archive-free restricted-role deletion, CSRF, scope, replay, stale session and deleted login.");
    }


internal sealed class FixtureEnvironment : IWebHostEnvironment
{
 public string ApplicationName{get;set;}="Synthetic";
 public string EnvironmentName{get;set;}="Testing";
 public string ContentRootPath{get;set;}="/recovery";
 public string WebRootPath{get;set;}="/recovery/wwwroot";
 public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
 public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}
}
