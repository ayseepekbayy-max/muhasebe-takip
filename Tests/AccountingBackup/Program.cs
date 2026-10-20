using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Pages.Araclar;
using MuhasebeTakip2.App.Services;

int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
var root = Path.Combine(Path.GetTempPath(), "backup-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var env = new TestEnvironment(root);
var interceptor = new FailureInterceptor();
var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={root}/test.db").AddInterceptors(interceptor).Options;
await using var db = new AppDbContext(options);
try
{
    await db.Database.EnsureCreatedAsync();
    var source = new Firma { FirmaAdi = "Source" };
    var target = new Firma { FirmaAdi = "Target" };
    var empty = new Firma { FirmaAdi = "Empty" };
    var legacyTarget = new Firma { FirmaAdi = "Legacy" };
    db.AddRange(source, target, empty, legacyTarget); await db.SaveChangesAsync();
    var user = new Kullanici { FirmaId = source.Id, KullaniciAdi = "operator", Email = "operator@example.test", Sifre = "NEVER_EXPORT_PASSWORD" };
    var targetUser = new Kullanici { FirmaId = target.Id, KullaniciAdi = user.KullaniciAdi, Email = user.Email, Sifre = "other" };
    db.AddRange(user, targetUser); await db.SaveChangesAsync();
    // One complete row in every explicitly supported table, plus two identical cash rows.
    Type[] types = [typeof(CariKart), typeof(StokUrun), typeof(Calisan), typeof(Musteri), typeof(Cek), typeof(FaturaNumaraAyari),
        typeof(OdemePlani), typeof(MaliyetKaydi), typeof(Fatura), typeof(MusteriIs), typeof(CalisanAvans), typeof(CalisanPuantaj),
        typeof(CalisanMaasArsiv), typeof(StokHareket), typeof(FaturaKalem), typeof(MusteriMasraf), typeof(KasaHareket),
        typeof(EkDosya), typeof(OdemeHareketi), typeof(OdemeBildirimGecmisi), typeof(OdemeBildirimGizleme), typeof(YoneticiNotu), typeof(IslemGecmisi)];
    var seeded = new Dictionary<string, object>();
    var links = new Dictionary<string, string> { ["CariKartId"]="CariKart", ["StokUrunId"]="StokUrun", ["CalisanId"]="Calisan", ["MusteriId"]="Musteri",
        ["MusteriIsId"]="MusteriIs", ["FaturaId"]="Fatura", ["OdemePlaniId"]="OdemePlani" };
    foreach (var type in types)
    {
        var entity = Activator.CreateInstance(type)!;
        foreach (var property in db.Model.FindEntityType(type)!.GetProperties().Where(x => x.PropertyInfo != null))
        {
            object? value = property.PropertyInfo!.GetValue(entity);
            if (property.Name == "FirmaId") value = source.Id;
            else if (property.Name is "KullaniciId" or "OlusturanKullaniciId") value = user.Id;
            else if (links.TryGetValue(property.Name, out var parent)) value = (int)seeded[parent].GetType().GetProperty("Id")!.GetValue(seeded[parent])!;
            else if (property.ClrType == typeof(decimal)) value = 123.45m;
            else if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)) value = new DateTime(2026, 10, 19, 13, 14, 15, DateTimeKind.Utc);
            else if (property.ClrType == typeof(string)) value = "test";
            else if (property.ClrType.IsEnum) value = Enum.ToObject(property.ClrType, 1);
            property.PropertyInfo.SetValue(entity, value);
        }
        // Include archived cards and employees instead of filtering them out.
        type.GetProperty("AktifMi")?.SetValue(entity, false);
        if (entity is EkDosya attachment) attachment.DosyaYolu = "/uploads/faturalar/fixture.pdf";
        if (entity is Cek check) check.ResimYolu = "/uploads/cekler/check.jpg";
        db.Add(entity); await db.SaveChangesAsync(); seeded[type.Name] = entity;
    }
    var sourceCash = (KasaHareket)seeded[nameof(KasaHareket)];
    db.Add(new KasaHareket { FirmaId = source.Id, CariKartId = sourceCash.CariKartId, FaturaId = sourceCash.FaturaId,
        Tarih = sourceCash.Tarih, Tip = sourceCash.Tip, Tutar = sourceCash.Tutar, Aciklama = sourceCash.Aciklama });
    db.Add(new CariKart { FirmaId = empty.Id, Unvan = "DO_NOT_EXPORT_OTHER_FIRM" });
    db.Add(new PrivateMessage { SenderPerson = 1, SenderName = "private", Content = "NEVER_EXPORT_CHAT" });
    await db.SaveChangesAsync();
    Directory.CreateDirectory(Path.Combine(env.WebRootPath, "uploads/faturalar"));
    Directory.CreateDirectory(Path.Combine(env.WebRootPath, "uploads/cekler"));
    await File.WriteAllBytesAsync(Path.Combine(env.WebRootPath, "uploads/faturalar/fixture.pdf"), Encoding.UTF8.GetBytes("PDF fixture bytes"));
    await File.WriteAllBytesAsync(Path.Combine(env.WebRootPath, "uploads/cekler/check.jpg"), [1, 2, 3, 4, 5]);
    var service = new AccountingBackupService(db, env);
    var bytes = await service.ExportAsync(source.Id);
    AccountingBackupService.Package Read(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        return JsonSerializer.Deserialize<AccountingBackupService.Package>(zip.GetEntry("manifest.json")!.Open())!;
    }
    byte[] Change(byte[] data, Action<AccountingBackupService.Package> mutate, bool corruptFile = false)
    {
        using var inputZip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        var package = Read(data); mutate(package);
        using var output = new MemoryStream();
        using (var outputZip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using (var m = outputZip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(m, package);
            foreach (var entry in inputZip.Entries.Where(x => x.FullName != "manifest.json"))
            {
                using var destination = outputZip.CreateEntry(entry.FullName).Open();
                using var from = entry.Open();
                if (corruptFile) destination.Write(new byte[entry.Length]); else from.CopyTo(destination);
            }
        }
        return output.ToArray();
    }
    var package = Read(bytes);
    var manifestText = JsonSerializer.Serialize(package);
    Check(package.Tables.Count == 23 && package.Tables.Values.Sum(x=>x.Count) == 24, "All 23 tables and duplicate cash included");
    Check(!manifestText.Contains("NEVER_EXPORT") && !manifestText.Contains("DO_NOT_EXPORT") && !manifestText.Contains("Sifre"), "Tenant isolation and no secrets/chat");
    Check(package.Documents.Count == 2, "Both attachment and check image packaged");
    Check(package.Tables["CariKart"][0]["AktifMi"].GetBoolean() == false, "Archived cards included");
    var result = await service.RestoreAsync(new MemoryStream(bytes), target.Id);
    Check(result.Records == 24 && result.Files == 2, "Full restore counts");
    db.ChangeTracker.Clear();
    var restoredInvoice = await db.Faturalar.SingleAsync(x=>x.FirmaId==target.Id);
    var restoredCari = await db.CariKartlar.SingleAsync(x=>x.FirmaId==target.Id);
    var restoredCash = await db.KasaHareketleri.Where(x=>x.FirmaId==target.Id).ToListAsync();
    Check(restoredInvoice.Id != ((Fatura)seeded["Fatura"]).Id && restoredInvoice.CariKartId == restoredCari.Id, "New IDs with invoice/customer link");
    Check(restoredCash.Count == 2 && restoredCash.All(x=>x.FaturaId==restoredInvoice.Id && x.CariKartId==restoredCari.Id), "Duplicate cash preserved with remapped links");
    var restoredEmployee = await db.Calisanlar.SingleAsync(x=>x.FirmaId==target.Id);
    Check((await db.CalisanMaasArsivleri.SingleAsync(x=>x.FirmaId==target.Id)).CalisanId == restoredEmployee.Id, "Undeclared salary employee link remapped");
    Check((await db.OdemeBildirimGecmisleri.SingleAsync(x=>x.FirmaId==target.Id)).KullaniciId == targetUser.Id, "User reference mapped within target firm");
    var restoredFile = await db.EkDosyalar.SingleAsync(x=>x.FirmaId==target.Id);
    Check(await File.ReadAllTextAsync(Path.Combine(env.WebRootPath, restoredFile.DosyaYolu.TrimStart('/'))) == "PDF fixture bytes", "Attachment bytes restored");
    var again = Read(await service.ExportAsync(target.Id));
    foreach (var table in package.Tables)
    {
        Check(again.Tables[table.Key].Count == table.Value.Count, $"Roundtrip count: {table.Key}");
        var entityType = db.Model.GetEntityTypes().Single(x=>x.ClrType.Name==table.Key);
        foreach (var prop in entityType.GetProperties().Where(x=>x.ClrType==typeof(decimal)))
            Check(table.Value.Sum(x=>x[prop.Name].GetDecimal()) == again.Tables[table.Key].Sum(x=>x[prop.Name].GetDecimal()), $"Roundtrip totals: {table.Key}.{prop.Name}");
    }
    async Task Rejected(byte[] data, int targetId, string label)
    {
        try { await service.RestoreAsync(new MemoryStream(data), targetId); throw new Exception("Accepted: " + label); }
        catch (AccountingBackupService.BackupException) { checks++; }
    }
    await Rejected(bytes, target.Id, "Repeated restore into populated target");
    await Rejected(bytes, empty.Id, "Existing unrelated data preserved");
    Check(await db.CariKartlar.AnyAsync(x=>x.FirmaId==empty.Id && x.Unvan=="DO_NOT_EXPORT_OTHER_FIRM"), "Existing data not deleted");
    await Rejected(Change(bytes, p=>p.Version=999), legacyTarget.Id, "Unsupported version");
    await Rejected(Change(bytes, p=>p.Tables["KasaHareket"][0]["FaturaId"]=JsonSerializer.SerializeToElement(99999)), legacyTarget.Id, "Dangling relationship");
    await Rejected(Change(bytes, p=>p.Documents[0]=p.Documents[0] with { Entry="files/../../outside.pdf" }), legacyTarget.Id, "ZIP traversal");
    await Rejected(Change(bytes, p=>{ }, true), legacyTarget.Id, "Corrupted document hash");
    await Rejected(Change(bytes, p=>p.Documents.Clear()), legacyTarget.Id, "Missing document manifest");
    // Force failure after many parent tables have been saved; all rows and files must roll back.
    var rollbackUser = new Kullanici { FirmaId=legacyTarget.Id, KullaniciAdi=user.KullaniciAdi, Email=user.Email };
    db.Add(rollbackUser); await db.SaveChangesAsync();
    interceptor.FailAfter = 8;
    try { await service.RestoreAsync(new MemoryStream(bytes), legacyTarget.Id); throw new Exception("Expected injected failure"); }
    catch (InjectedFailure) { checks++; }
    interceptor.FailAfter = -1;
    Check(!await db.CariKartlar.AnyAsync(x=>x.FirmaId==legacyTarget.Id) && !await db.OdemePlanlari.AnyAsync(x=>x.FirmaId==legacyTarget.Id), "Failed restore rolls back already saved parents");
    Check(!Directory.Exists(Path.Combine(root,"App_Data/RestoreStaging")) || !Directory.EnumerateDirectories(Path.Combine(root,"App_Data/RestoreStaging")).Any(), "Staging cleaned on failure");
    // File placement failure after database inserts must also roll back the entire restore.
    var failedEnvironment = new TestEnvironment(Path.Combine(root, "file-failure"));
    Directory.CreateDirectory(Path.Combine(failedEnvironment.WebRootPath, "uploads"));
    await File.WriteAllTextAsync(Path.Combine(failedEnvironment.WebRootPath, "uploads/yedek"), "blocks directory creation");
    try { await new AccountingBackupService(db, failedEnvironment).RestoreAsync(new MemoryStream(bytes), legacyTarget.Id); throw new Exception("File failure accepted"); }
    catch (IOException) { checks++; }
    Check(!await db.CariKartlar.AnyAsync(x=>x.FirmaId==legacyTarget.Id), "File placement failure rolls back rows");
    Check(!Directory.EnumerateFiles(failedEnvironment.WebRootPath,"*.pdf",SearchOption.AllDirectories).Any(), "File placement failure leaves no restored document");
    var missingUserTarget = new Firma { FirmaAdi="Missing users" };
    db.Add(missingUserTarget); await db.SaveChangesAsync();
    await Rejected(bytes, missingUserTarget.Id, "Required user mapping must not silently change ownership");
    Check(!await db.Faturalar.AnyAsync(x=>x.FirmaId==missingUserTarget.Id), "Missing required user rolls back earlier tables");
    await Rejected(Change(bytes,p=>p.Tables["PrivateMessage"]=[]), legacyTarget.Id, "Unknown table allowlist");
    try { await AccountingBackupService.CopyLimitedAsync(new MemoryStream([1,2,3]),new MemoryStream(),2,default); throw new Exception("Size limit ignored"); }
    catch (AccountingBackupService.BackupException) { checks++; }
    // Legacy JSON includes cash rows, preserves multiplicity, is repeat-safe, and warns about missing scope.
    var legacy = new YedeklemeModel.YedekPaketi
    {
        Cariler = [new("Legacy Cari", "Legacy Cari", null, null, CariTip.Alici, DateTime.UtcNow)],
        KasaHareketleri = [new(DateTime.UtcNow.Date, HareketTipi.Giris, 42, "legacy", "Legacy Cari"), new(DateTime.UtcNow.Date, HareketTipi.Giris, 42, "legacy", "Legacy Cari")]
    };
    YedeklemeModel Page(byte[] json)
    {
        var http = new DefaultHttpContext { Session = new TestSession() }; http.Session.SetInt32("FirmaId", legacyTarget.Id);
        return new YedeklemeModel(db, env, NullLogger<YedeklemeModel>.Instance)
        {
            PageContext = new PageContext { HttpContext=http },
            YedekDosyasi = new FormFile(new MemoryStream(json),0,json.Length,"YedekDosyasi","legacy.json")
        };
    }
    var legacyJson = JsonSerializer.SerializeToUtf8Bytes(legacy);
    var page = Page(legacyJson); await page.OnPostGeriYukleAsync();
    Check(page.Hata == "" && page.Mesaj.Contains("eksik kapsam"), "Legacy format warning");
    Check(await db.KasaHareketleri.CountAsync(x=>x.FirmaId==legacyTarget.Id) == 2, "Legacy cash multiplicity restored");
    await Page(legacyJson).OnPostGeriYukleAsync();
    Check(await db.KasaHareketleri.CountAsync(x=>x.FirmaId==legacyTarget.Id) == 2, "Legacy repeat does not duplicate cash");
    var invalid = Page(Encoding.UTF8.GetBytes("{}")); await invalid.OnPostGeriYukleAsync();
    Check(invalid.Hata.Length > 0, "Unrecognized JSON rejected");
    // Failure after a legacy parent insert is rolled back too.
    legacy.Cariler = [new("Rollback Cari", "Rollback Cari", null, null, CariTip.Alici, DateTime.UtcNow)];
    legacy.KasaHareketleri = [new(DateTime.UtcNow.Date,HareketTipi.Giris,1,"bad","Missing Cari")];
    var badLegacy=Page(JsonSerializer.SerializeToUtf8Bytes(legacy)); await badLegacy.OnPostGeriYukleAsync();
    Check(badLegacy.Hata.Length > 0 && !await db.CariKartlar.AnyAsync(x=>x.FirmaId==legacyTarget.Id && x.Unvan=="Rollback Cari"), "Legacy rollback preserves all-or-nothing behavior");
    // Diagnose orphan salary links without guessing the owner or changing the source records.
    var salary = await db.CalisanMaasArsivleri.SingleAsync(x=>x.FirmaId==source.Id);
    var originalEmployeeId = salary.CalisanId;
    salary.CalisanId = 987654;
    await db.SaveChangesAsync();
    var diagnosisHttp = new DefaultHttpContext { Session = new TestSession() };
    diagnosisHttp.Session.SetInt32("FirmaId", source.Id);
    var diagnosisPage = new YedeklemeModel(db, env, NullLogger<YedeklemeModel>.Instance)
        { PageContext = new PageContext { HttpContext = diagnosisHttp } };
    await diagnosisPage.OnGetIndirAsync();
    Check(diagnosisPage.BaglantiHatalari.Count == 1, "Export reports structured orphan link");
    var issue = diagnosisPage.BaglantiHatalari.Single();
    Check(issue.RecordType == "Maaş arşivi" && issue.RecordId == salary.Id && issue.Field == "CalisanId" &&
        issue.RelatedType == "Çalışan" && issue.RelatedId == 987654, "Diagnosis identifies exact own record and missing reference");
    Check(diagnosisPage.Hata.Contains("kesinleştirmez") && !diagnosisPage.Hata.Contains("DO_NOT_EXPORT"), "No unsupported cross-company claim or external record data");
    Check((await db.CalisanMaasArsivleri.AsNoTracking().SingleAsync(x=>x.Id==salary.Id)).CalisanId==987654,
        "Read-only diagnosis never repairs source data");
    salary.CalisanId = originalEmployeeId; await db.SaveChangesAsync();
    var manyBrokenLinks = Change(bytes, p =>
    {
        for (var i=0;i<21;i++)
        {
            var row = new Dictionary<string,JsonElement>(p.Tables["KasaHareket"][0]);
            row["Id"]=JsonSerializer.SerializeToElement(100000+i);
            row["FaturaId"]=JsonSerializer.SerializeToElement(900000+i);
            p.Tables["KasaHareket"].Add(row);
        }
    });
    try { await service.RestoreAsync(new MemoryStream(manyBrokenLinks),missingUserTarget.Id); throw new Exception("Broken links accepted"); }
    catch (AccountingBackupService.BackupException ex)
    {
        Check(ex.LinkIssues.Count==20 && ex.Message.Contains("21 sorun"), "Bounded diagnostics retain total count");
        Check(ex.LinkIssues.All(x=>x.RecordType=="Kasa hareketi" && x.Field=="FaturaId"), "All listed diagnostics use known schema labels");
    }
    Check(!await db.KasaHareketleri.AnyAsync(x=>x.FirmaId==missingUserTarget.Id), "Restore diagnostics stop before any writes");
    // Missing source documents must not produce a supposedly complete backup.
    File.Delete(Path.Combine(env.WebRootPath,"uploads/faturalar/fixture.pdf"));
    try { await service.ExportAsync(source.Id); throw new Exception("Missing document accepted"); }
    catch (Exception ex) when (ex is AccountingBackupService.BackupException or FileNotFoundException) { checks++; }
    Console.WriteLine($"PASS: {checks} backup checks across 23 tables, file integrity, tenant isolation, rollback, and legacy restore.");
}
finally { await db.Database.CloseConnectionAsync(); Directory.Delete(root, true); }

sealed class InjectedFailure : Exception { }
sealed class FailureInterceptor : SaveChangesInterceptor
{
    public int FailAfter = -1;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct=default)
    {
        if (FailAfter >= 0 && FailAfter-- == 0) throw new InjectedFailure();
        return ValueTask.FromResult(result);
    }
}
sealed class TestSession : ISession
{
    readonly Dictionary<string,byte[]> data=new(); public bool IsAvailable=>true; public string Id=>"test"; public IEnumerable<string> Keys=>data.Keys;
    public void Clear()=>data.Clear(); public Task CommitAsync(CancellationToken ct=default)=>Task.CompletedTask;
    public Task LoadAsync(CancellationToken ct=default)=>Task.CompletedTask; public void Remove(string key)=>data.Remove(key);
    public void Set(string key,byte[] value)=>data[key]=value; public bool TryGetValue(string key,out byte[] value)=>data.TryGetValue(key,out value!);
}
sealed class TestEnvironment : IWebHostEnvironment
{
    public TestEnvironment(string root) { ContentRootPath=root; WebRootPath=Path.Combine(root,"wwwroot"); Directory.CreateDirectory(WebRootPath); }
    public string ApplicationName {get;set;}="Test"; public string EnvironmentName {get;set;}="Test";
    public string ContentRootPath {get;set;} public string WebRootPath {get;set;}
    public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider(); public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
}
