using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

if (!OperatingSystem.IsLinux() || args is not [("write" or "wrongkey" or "restore")])
    throw new InvalidOperationException("Run only in the isolated fixture.");

const string disk = "/work/documents";
var dbConnection = "Host=/work;Database=" + (args[0] == "write" ? "firmova_stage8_synthetic" : "firmova_stage8_recovery") + ";Username=postgres";
var write = args[0] == "write";
const bool privateEnabled = true;
Directory.CreateDirectory(disk);
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dbConnection).Options;
await using var db = new AppDbContext(options);
if (write || args[0] == "wrongkey")
{
    await db.Database.EnsureCreatedAsync();
    db.Firmalar.AddRange(new Firma { Id = 71, FirmaAdi = "Synthetic A" }, new Firma { Id = 72, FirmaAdi = "Synthetic B" });
    db.Kullanicilar.AddRange(new Kullanici { Id = 101, FirmaId = 71, KullaniciAdi = "Synthetic A" },
        new Kullanici { Id = 102, FirmaId = 72, KullaniciAdi = "Synthetic B" });
    if (write) db.CariKartlar.Add(new CariKart { Id = 11, FirmaId = 71, Unvan = "Synthetic A", AktifMi = true, OlusturmaTarihi = DateTime.UtcNow });
    if (write) db.Faturalar.Add(new Fatura { Id = 22, FirmaId = 71, CariKartId = 11, FaturaNo = "Synthetic A", AktifMi = true });
    await db.SaveChangesAsync();
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    ApplicationName = typeof(AppDbContext).Assembly.GetName().Name, WebRootPath = disk, EnvironmentName = "Testing", Args = []
});
builder.Configuration.Sources.Clear();
builder.Configuration.AddEnvironmentVariables();
if (builder.Configuration.GetValue<bool>("CompanyDocuments:WritePrivate") != privateEnabled ||
    builder.Configuration.GetValue<bool>("CompanyDeletion:Enabled"))
    throw new InvalidOperationException("Unexpected fixture flag.");
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();
builder.Services.AddRazorPages().AddMvcOptions(options => options.Filters.Add(new SwitchedAccountingBoundary.HandlerFilter()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddAdminPortalAuthorization();
builder.Services.AddAccountingContextFoundation();
builder.Services.AddScoped<IIslemGecmisiService, IslemGecmisiService>();
builder.Services.AddScoped<IOdemeBildirimService, OdemeBildirimService>();
builder.Services.AddScoped<ICekDurumService, CekDurumService>();
builder.Services.AddScoped<AppDbContext>(_ => new AppDbContext(options));

await using var app = builder.Build();
app.UseAccountingPublicAssets();
app.UseRouting();
app.UseAccountingRecoveryAdmission();
app.UseSession();
app.UseSwitchedAccountingBoundary();
app.MapAccountingDocuments();
app.MapRazorPages();
app.MapGet("/fixture/{person:int}", async (int person, HttpContext http, AccountingSessionLifecycle lifecycle) =>
{
    if (person is not (1 or 2)) return Results.NotFound();
    await lifecycle.ReplaceAsync(http, () =>
    {
        http.Session.SetInt32("KullaniciId", 100 + person);
        http.Session.SetInt32("FirmaId", 70 + person);
        return Task.CompletedTask;
    }, establishAccounting: true);
    return Results.Ok();
});
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
using var owner = Client();
using var stranger = Client();
using var anonymous = Client();
Check((await owner.GetAsync("/fixture/1")).IsSuccessStatusCode, "owner fixture session");
Check((await stranger.GetAsync("/fixture/2")).IsSuccessStatusCode, "other company fixture session");

var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aQ34AAAAASUVORK5CYII=");
var pdf = "%PDF-1.4\n% synthetic attachment\n%%EOF\n"u8.ToArray();

if (write)
{
    await Upload("/Cekler", "/Cekler", "CekResmi", "synthetic.png", image, new()
    {
        ["YeniCek.No"] = "STAGE8-SYNTHETIC", ["YeniCek.Tarih"] = "2026-01-01",
        ["YeniCek.Tip"] = "1", ["TutarGirisi"] = "42,00"
    });
    await Upload("/CariKartlar/Ekstre/11", "/CariKartlar/Ekstre/11?handler=DosyaEkle", "EkDosya", "synthetic.pdf", pdf, new());
    await Upload("/Faturalar/Detay/22", "/Faturalar/Detay/22?handler=DosyaEkle", "EkDosya", "synthetic.pdf", pdf, new());
}
const string page = "/Araclar/Yedekleme";
const string version = "synthetic-v1";
var idFile = "/work/archive-id";
var download = "/work/download.firmova-recovery";
if (write)
{
    using var created = await BackupPost(owner, "SifreliArsivOlustur", new());
    var html = await created.Content.ReadAsStringAsync();
    var id = Regex.Match(WebUtility.HtmlDecode(html), "name=\"archiveId\"[^>]*value=\"([0-9a-f-]{36})\"").Groups[1].Value;
    Check(created.IsSuccessStatusCode && Guid.TryParse(id,out _), "archive is created through Razor POST");
    await File.WriteAllTextAsync(idFile,id);
    var argsForArchive = ArchiveForm(id);
    using var downloaded = await BackupPost(owner,"SifreliArsivIndir",argsForArchive);
    var bytes = await downloaded.Content.ReadAsByteArrayAsync();
    var stored = Directory.GetFiles(Path.Combine(disk,"uploads/.firmova-private/recovery"),"accounting-*.firmova-recovery").Single();
    var storedBytes = await File.ReadAllBytesAsync(stored);
    Check(downloaded.IsSuccessStatusCode && downloaded.Content.Headers.ContentType?.MediaType=="application/octet-stream" &&
        bytes.SequenceEqual(storedBytes) && !bytes.AsSpan().StartsWith("PK"u8), "downloaded envelope integrity and encrypted format");
    await File.WriteAllBytesAsync(download,bytes);
    Check(CompanyAccountingRecovery.TryReadDownloadName($"firmova-{Guid.Parse(id):N}-key-{version}.firmova-recovery",out var parsedId,out var parsedVersion) && parsedId==Guid.Parse(id) && parsedVersion==version,"original download name preserves operation and version");
    using(var testArchive=CompanyRecoveryArchive.OnCompanyDisk(app.Environment,new byte[32],version))
    {
        var service=new CompanyAccountingRecovery(new AccountingBackupService(db,app.Environment,writePrivate:true),testArchive);
        try{await service.RestoreUploadedAsync(71,Guid.Parse(id),Stream.Null,CompanyRecoveryArchive.MaxAccountingEnvelopeBytes+1,default);throw new Exception("Oversize accepted");}
        catch(InvalidDataException){Check(true,"oversize rejected before reading upload");}
        try{await service.RestoreUploadedAsync(71,Guid.Parse(id),new MemoryStream([1,2]),31,default);throw new Exception("Truncated accepted");}
        catch(EndOfStreamException){Check(true,"short upload stream rejected");}
        try{await service.RestoreUploadedAsync(71,Guid.Parse(id),new MemoryStream(bytes),bytes.Length-1,default);throw new Exception("Length mismatch accepted");}
        catch(InvalidDataException){Check(true,"declared upload length mismatch rejected");}
    }
    foreach(var (client,confirmed,description) in new[] { (owner,false,"upload without confirmation"), (owner,true,"upload into nonempty target"), (stranger,true,"foreign-company upload") })
    {
        using var rejected=await UploadArchive(client,bytes,id,confirmed);
        Check(!WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync()).Contains("Şifreli arşiv geri yüklendi."),description+" rejected");
    }
    using(var badName=await UploadArchive(owner,bytes,id,true,"../../unsafe.firmova-recovery"))
        Check(WebUtility.HtmlDecode(await badName.Content.ReadAsStringAsync()).Contains("arşiv kimliğini"),"path-like filename rejected");
    using(var wrongVersion=await UploadArchive(stranger,bytes,id,true,$"firmova-{Guid.Parse(id):N}-key-synthetic-v2.firmova-recovery"))
        Check(WebUtility.HtmlDecode(await wrongVersion.Content.ReadAsStringAsync()).Contains("Şifreli arşiv doğrulanamadı"),"wrong authenticated version rejected");
    using(var noCsrfForm=new MultipartFormDataContent())
    {
        noCsrfForm.Add(new ByteArrayContent(bytes),"recoveryFile",$"firmova-{Guid.Parse(id):N}-key-{version}.firmova-recovery");
        noCsrfForm.Add(new StringContent("true"),"confirmRestore");
        using var noCsrf=await owner.PostAsync(page+"?handler=SifreliDosyaGeriYukle",noCsrfForm);
        Check(noCsrf.StatusCode==HttpStatusCode.BadRequest,"uploaded restore requires antiforgery token");
    }
    using(var denied=await anonymous.GetAsync(page))Check(!denied.IsSuccessStatusCode,"anonymous backup screen denied");
    using var zip = await BackupPost(owner,"Indir",new());
    var zipBytes = await zip.Content.ReadAsByteArrayAsync();
    Check(zip.IsSuccessStatusCode && zipBytes.AsSpan().StartsWith("PK"u8), "legacy ZIP export");
    await File.WriteAllBytesAsync("/work/download.zip",zipBytes);
    foreach(var (who,form,desc) in new[] { (owner,argsForArchive,"missing confirmation"),
        (owner,ArchiveForm(id,true),"nonempty target"), (stranger,ArchiveForm(id,true),"foreign company") })
    {
        using var rejected = await BackupPost(who,"SifreliArsivGeriYukle",form);
        Check(!(WebUtility.HtmlDecode(await rejected.Content.ReadAsStringAsync())).Contains("Şifreli arşiv geri yüklendi."),desc+" rejected");
    }
    using var foreignDownload = await BackupPost(stranger,"SifreliArsivIndir",argsForArchive);
    Check(foreignDownload.Content.Headers.ContentType?.MediaType!="application/octet-stream","foreign company cannot download");
    Check(await db.Cekler.CountAsync()==1 && await db.EkDosyalar.CountAsync()==2,"nonempty and foreign attempts preserved records");
}
else
{
    var id = await File.ReadAllTextAsync(idFile);
    var bytes = await File.ReadAllBytesAsync(download);
    var archivePath = Path.Combine(disk,"uploads/.firmova-private/recovery",
        $"accounting-71-{Guid.Parse(id):N}-key-{version}.firmova-recovery");
    Check(!await db.Cekler.AnyAsync() && !await db.EkDosyalar.AnyAsync(),"empty recovery database");
    if (args[0]=="wrongkey")
    {
        File.Delete(archivePath);
        using (var wrongArchive = new CompanyRecoveryArchive(Path.GetDirectoryName(archivePath)!, Enumerable.Repeat((byte)1,32).ToArray(), version))
            wrongArchive.SaveAccountingPackage(71, Guid.Parse(id), await File.ReadAllBytesAsync("/work/download.zip"));
        using var bad = await BackupPost(owner,"SifreliArsivGeriYukle",ArchiveForm(id,true));
        Check((WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync())).Contains("Şifreli arşiv doğrulanamadı"),"wrong key rejected");
        Check(!await db.Cekler.AnyAsync() && !await db.EkDosyalar.AnyAsync(),"wrong key wrote no records");
        using var uploadBad=await UploadArchive(owner,await File.ReadAllBytesAsync(archivePath),id,true);
        Check(WebUtility.HtmlDecode(await uploadBad.Content.ReadAsStringAsync()).Contains("Şifreli arşiv doğrulanamadı"),"uploaded wrong-key ciphertext rejected");
        Check(!await db.Cekler.AnyAsync() && !await db.EkDosyalar.AnyAsync(),"wrong-key upload wrote no records");
    }
    else
    {
        bytes[^1] ^= 1;
        await File.WriteAllBytesAsync(archivePath,bytes);
        using var bad = await BackupPost(owner,"SifreliArsivGeriYukle",ArchiveForm(id,true));
        Check((WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync())).Contains("Şifreli arşiv doğrulanamadı"),"corrupt archive rejected");
        Check(!await db.Cekler.AnyAsync() && !await db.EkDosyalar.AnyAsync(),"corrupt archive wrote no records");
        using(var corruptUpload=await UploadArchive(owner,bytes,id,true))
            Check(WebUtility.HtmlDecode(await corruptUpload.Content.ReadAsStringAsync()).Contains("Şifreli arşiv doğrulanamadı"),"corrupt upload rejected");
        using(var paddedUpload=await UploadArchive(owner,new byte[70000],id,true))
            Check(WebUtility.HtmlDecode(await paddedUpload.Content.ReadAsStringAsync()).Contains("Şifreli arşiv doğrulanamadı"),"buffered upload above default disk-spill threshold rejected safely");
        Check(!await db.Cekler.AnyAsync() && !await db.EkDosyalar.AnyAsync(),"rejected uploads leave empty target unchanged");
        File.Delete(archivePath);
        Check(!File.Exists(archivePath),"server original absent before uploaded restore");
        var downloadedBytes=await File.ReadAllBytesAsync(download);
        using(var wrongVersion=await UploadArchive(owner,downloadedBytes,id,true,$"firmova-{Guid.Parse(id):N}-key-synthetic-v2.firmova-recovery"))
            Check(WebUtility.HtmlDecode(await wrongVersion.Content.ReadAsStringAsync()).Contains("Şifreli arşiv doğrulanamadı"),"version substitution rejected on empty same-company target");
        using(var wrongOperation=await UploadArchive(owner,downloadedBytes,id,true,$"firmova-{Guid.NewGuid():N}-key-{version}.firmova-recovery"))
            Check(WebUtility.HtmlDecode(await wrongOperation.Content.ReadAsStringAsync()).Contains("Şifreli arşiv doğrulanamadı"),"operation substitution rejected");
        var configuration=app.Services.GetRequiredService<IConfiguration>();
        configuration["CompanyRecovery:Enabled"]="false";
        using(var disabled=await UploadArchive(owner,downloadedBytes,id,true))
            Check(!WebUtility.HtmlDecode(await disabled.Content.ReadAsStringAsync()).Contains("Şifreli arşiv geri yüklendi."),"feature-disabled upload rejected");
        configuration["CompanyRecovery:Enabled"]="true";
        Check(!await db.Cekler.AnyAsync() && !await db.EkDosyalar.AnyAsync(),"identity and feature rejections leave empty target unchanged");
        using(var changedNameWrongIdentity=await BackupPost(owner,"SifreliDosyaGeriYukle",new(){["confirmRestore"]="true",["uploadedArchiveId"]=Guid.NewGuid().ToString(),["uploadedKeyVersion"]=version},downloadedBytes,"renamed.firmova-recovery","recoveryFile"))
            Check(WebUtility.HtmlDecode(await changedNameWrongIdentity.Content.ReadAsStringAsync()).Contains("Şifreli arşiv doğrulanamadı"),"renamed file with wrong explicit identity rejected");
        using(var conflicting=await BackupPost(owner,"SifreliDosyaGeriYukle",new(){["confirmRestore"]="true",["uploadedArchiveId"]=Guid.NewGuid().ToString(),["uploadedKeyVersion"]=version},downloadedBytes,$"firmova-{Guid.Parse(id):N}-key-{version}.firmova-recovery","recoveryFile"))
            Check(!WebUtility.HtmlDecode(await conflicting.Content.ReadAsStringAsync()).Contains("Şifreli arşiv geri yüklendi."),"filename and explicit metadata conflict rejected");
        using var restored=await BackupPost(owner,"SifreliDosyaGeriYukle",new(){["confirmRestore"]="true",["uploadedArchiveId"]=id,["uploadedKeyVersion"]=version},downloadedBytes,"renamed.firmova-recovery","recoveryFile");
        Check(WebUtility.HtmlDecode(await restored.Content.ReadAsStringAsync()).Contains("Şifreli arşiv geri yüklendi."),"downloaded copy restores without server original through confirmed upload");
        Check(WebUtility.HtmlDecode(await restored.Content.ReadAsStringAsync()).Contains("Şifreli arşiv geri yüklendi."),"renamed file restores with authenticated explicit metadata");
        var cheque = await db.Cekler.AsNoTracking().SingleAsync();
        var attachments = await db.EkDosyalar.AsNoTracking().OrderBy(x=>x.Id).ToArrayAsync();
        Check(attachments.Length==2 && cheque.FirmaId==71 && attachments.All(x=>x.FirmaId==71),"restored tenant and document links");
        Check((await owner.GetByteArrayAsync("/belgeler/cek/"+cheque.Id)).SequenceEqual(image),"restored cheque image bytes");
        foreach(var attachment in attachments)
            Check((await owner.GetByteArrayAsync("/belgeler/ek/"+attachment.Id)).SequenceEqual(pdf),"restored attachment bytes");
        using(var replay=await UploadArchive(owner,downloadedBytes,id,true))
            Check(!WebUtility.HtmlDecode(await replay.Content.ReadAsStringAsync()).Contains("Şifreli arşiv geri yüklendi."),"repeat upload rejected on occupied target");
        Check(await db.Cekler.CountAsync()==1 && await db.EkDosyalar.CountAsync()==2,"repeat upload preserves record counts");
        Check(!File.Exists(archivePath),"uploaded restore does not recreate server archive");
        if(Environment.GetEnvironmentVariable("ASPNETCORE_TEMP") is string temp)
            Check(!Directory.EnumerateFileSystemEntries(temp).Any(),"upload leaves no temporary files");

        using var zip = await BackupPost(stranger,"GeriYukle",new(),await File.ReadAllBytesAsync("/work/download.zip"),"synthetic.zip");
        Check((WebUtility.HtmlDecode(await zip.Content.ReadAsStringAsync())).Contains("Tam yedek geri yüklendi:"),"legacy ZIP import");
        var legacy = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
            OlusturmaTarihi=DateTime.UtcNow,
            Cariler=new[] { new {Unvan="Synthetic JSON", Ad="Synthetic JSON", Tip=1, OlusturmaTarihi=DateTime.UtcNow}},
            Stoklar=Array.Empty<object>(), Faturalar=Array.Empty<object>(), KasaHareketleri=Array.Empty<object>(), Cekler=Array.Empty<object>()});
        using var json = await BackupPost(stranger,"GeriYukle",new(){["confirmLegacyImport"]="true"},legacy,"synthetic.json");
        Check(await db.CariKartlar.AsNoTracking().AnyAsync(x=>x.FirmaId==72 && x.Unvan=="Synthetic JSON"),"legacy JSON import via real handler");
    }
}
await app.StopAsync();
Console.WriteLine("PASS PHASE " + args[0]);

Dictionary<string,string> ArchiveForm(string id,bool confirmed=false)
{
    var result = new Dictionary<string,string> { ["archiveId"]=id, ["keyVersion"]=version };
    if(confirmed) result["confirmRestore"]="true";
    return result;
}

Task<HttpResponseMessage> UploadArchive(HttpClient client,byte[] bytes,string id,bool confirm,string? filename=null) =>
    BackupPost(client,"SifreliDosyaGeriYukle",confirm?new(){["confirmRestore"]="true"}:new(),bytes,
        filename??$"firmova-{Guid.Parse(id):N}-key-{version}.firmova-recovery","recoveryFile");

async Task<HttpResponseMessage> BackupPost(HttpClient client,string handler,Dictionary<string,string> fields,byte[]? file=null,string? filename=null,string fileField="YedekDosyasi")
{
    var html = await client.GetStringAsync(page);
    using var form = new MultipartFormDataContent();
    foreach(var name in new[]{"__RequestVerificationToken","AccountingCompanyId","AccountingVersion"})
    {
        var match=Regex.Match(html,"name=\""+Regex.Escape(name)+"\"[^>]*value=\"([^\"]+)\"");
        if(!match.Success) throw new InvalidOperationException("Missing form value: "+name);
        form.Add(new StringContent(WebUtility.HtmlDecode(match.Groups[1].Value)),name);
    }
    foreach(var (name,value) in fields) form.Add(new StringContent(value),name);
    if(file is not null) form.Add(new ByteArrayContent(file),fileField,filename!);
    return await client.PostAsync(page+"?handler="+handler,form);
}


async Task Upload(string formUrl, string uploadUrl, string fileField, string filename, byte[] bytes, Dictionary<string,string> values)
{
    using var formResponse = await owner.GetAsync(formUrl);
    Check(formResponse.IsSuccessStatusCode, "GET " + formUrl + " " + formResponse.StatusCode);
    var html = await formResponse.Content.ReadAsStringAsync();
    using var form = new MultipartFormDataContent();
    foreach (var name in new[] {"__RequestVerificationToken", "AccountingCompanyId", "AccountingVersion"})
    {
        var match = Regex.Match(html, "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]+)\"");
        if (match.Success) form.Add(new StringContent(WebUtility.HtmlDecode(match.Groups[1].Value)), name);
        else if (name == "__RequestVerificationToken") throw new InvalidOperationException("Missing antiforgery token.");
    }
    foreach (var (name,value) in values) form.Add(new StringContent(value), name);
    using var file = new ByteArrayContent(bytes);
    file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(filename.EndsWith(".png") ? "image/png" : "application/pdf");
    form.Add(file,fileField,filename);
    using var response = await owner.PostAsync(uploadUrl,form);
    Check(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect, "POST " + uploadUrl + " " + response.StatusCode);
}
static void Check(bool pass,string label)
{
    if (!pass) throw new InvalidOperationException(label);
    Console.WriteLine("PASS " + label);
}
