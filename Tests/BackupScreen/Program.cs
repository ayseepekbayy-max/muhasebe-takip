using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

if (!OperatingSystem.IsLinux() || args is not ["ui"])
    throw new InvalidOperationException("Run only in the isolated fixture.");

var checks=0;
const string disk = "/work/documents";
var dbConnection = "Host=/work;Database=firmova_backup_screen_synthetic;Username=postgres";
var write = true;
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
builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> {
 ["CompanyDocuments:WritePrivate"]="true",["CompanyDeletion:Enabled"]="false",
 ["CompanyRecovery:Enabled"]="true",["CompanyRecovery:KeyVersion"]="synthetic-ui" });
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

const string page="/Araclar/Yedekleme";

void Check(bool ok,string name){if(!ok)throw new InvalidDataException(name);checks++;Console.WriteLine("PASS "+name);}
string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+Regex.Escape(name)+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
async Task<(HttpStatusCode Status,string Html)> Result(HttpResponseMessage response)
{using(response)return(response.StatusCode,WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));}
async Task<HttpResponseMessage> Post(HttpClient client,string handler,Dictionary<string,string>? fields=null,byte[]? bytes=null,string filename="backup.zip",string fileField="recoveryFile",bool csrf=true)
{
 var html=await client.GetStringAsync(page);using var form=new MultipartFormDataContent();
 foreach(var name in new[]{"__RequestVerificationToken","AccountingCompanyId","AccountingVersion"})
 {if(!csrf && name=="__RequestVerificationToken")continue;var value=Field(html,name);Check(value.Length>0,"form context "+name);form.Add(new StringContent(value),name);}
 if(fields is not null)foreach(var (k,v) in fields)form.Add(new StringContent(v),k);
 if(bytes is not null)form.Add(new ByteArrayContent(bytes),fileField,filename);
 return await client.PostAsync(page+"?handler="+handler,form);
}
async Task<string> State()=>await db.Database.SqlQueryRaw<string>("""
 SELECT concat((SELECT count(*) FROM "CariKartlar"),':',(SELECT count(*) FROM "Faturalar"),':',
 (SELECT count(*) FROM "Cekler"),':',(SELECT count(*) FROM "EkDosyalar")) AS "Value"
 """).SingleAsync();
var config=app.Services.GetRequiredService<IConfiguration>();
var screen=await owner.GetStringAsync(page);
Check(Regex.Matches(screen,"data-backup-section=").Count==2,"exactly two main workflow sections");
Check(screen.Contains("Yedek Oluştur") && screen.Contains("Yedekten Geri Yükle") && screen.Contains("Synthetic A"),"clear main actions and authorized target company");
Check(screen.Contains("value=\"encrypted\" checked=\"checked\""),"encrypted backup selected by default");
Check(Regex.Matches(screen,"name=\"recoveryFile\"").Count==1,"one file input shared by encrypted and ZIP restore");
var advanced=screen.IndexOf("id=\"advanced-backup-options\"",StringComparison.Ordinal);
Check(advanced>=0 && screen.IndexOf("ZIP Yedeği İndir",StringComparison.Ordinal)>advanced && screen.IndexOf("name=\"YedekDosyasi\"",StringComparison.Ordinal)>advanced && screen.IndexOf("name=\"archiveId\"",StringComparison.Ordinal)>advanced,"ZIP creation, JSON and server archive controls under advanced options");
Check(screen.Contains("tam geri yükleme değildir") && screen.Contains("mevcut") && screen.Contains("name=\"confirmLegacyImport\""),"legacy import has warning and separate consent");
Check(screen.Contains("<details") && screen.Contains("uploadedArchiveId") && screen.Contains("uploadedKeyVersion"),"renamed archive identity remains available in optional details");
using(var denied=await anonymous.GetAsync(page))Check(!denied.IsSuccessStatusCode,"anonymous cannot open backup screen");

// Seed synthetic records directly; normal document upload is an unchanged, previously tested feature.
var checkPath="/uploads/.firmova-private/objects/cekler/"+Guid.NewGuid()+".png";
var checkFile=(PhysicalPath:disk+checkPath,StoredPath:checkPath);
Directory.CreateDirectory(Path.GetDirectoryName(checkFile.PhysicalPath)!);
var attachmentPath="/uploads/.firmova-private/objects/faturalar/fatura-22-"+Guid.NewGuid().ToString("N")+".pdf";
var attachmentFile=(PhysicalPath:disk+attachmentPath,StoredPath:attachmentPath);
Directory.CreateDirectory(Path.GetDirectoryName(attachmentFile.PhysicalPath)!);
await File.WriteAllBytesAsync(checkFile.PhysicalPath,image);await File.WriteAllBytesAsync(attachmentFile.PhysicalPath,pdf);
db.Add(new Cek{Id=33,FirmaId=71,No="UI-SYNTHETIC",Tip=CekTipi.Alinacak,Tarih=DateTime.UtcNow,Tutar=42,ResimYolu=checkFile.StoredPath});
db.Add(new EkDosya{Id=44,FirmaId=71,FaturaId=22,DosyaYolu=attachmentFile.StoredPath,DosyaAdi="synthetic.pdf",IcerikTipi="application/pdf",Boyut=pdf.Length,YuklemeTarihi=DateTime.UtcNow});
await db.SaveChangesAsync();
var created=await Result(await Post(owner,"SifreliArsivOlustur"));
var operation=Field(created.Html,"archiveId");var keyVersion=Field(created.Html,"keyVersion");
Check(Guid.TryParse(operation,out _) && keyVersion=="synthetic-ui" && created.Html.Contains("Yedeği İndir"),"create leads to download within create section; status="+created.Status+" alert="+Regex.Match(created.Html,"class=\"app-alert app-alert-danger\"[^>]*>([^<]*)").Groups[1].Value);
Check(!created.Html.Contains("Arşiv kimliği "+operation),"technical operation ID removed from main success text");
byte[] encrypted;using(var download=await Post(owner,"SifreliArsivIndir",new(){["archiveId"]=operation,["keyVersion"]=keyVersion}))
{Check(download.IsSuccessStatusCode && download.Content.Headers.ContentType?.MediaType=="application/octet-stream","existing verified encrypted download handler");encrypted=await download.Content.ReadAsByteArrayAsync();}
byte[] zip;using(var download=await Post(owner,"Indir")){zip=await download.Content.ReadAsByteArrayAsync();Check(zip.AsSpan().StartsWith("PK"u8),"ZIP creation remains available without encryption fallback");}
var archiveName=$"firmova-{Guid.Parse(operation):N}-key-{keyVersion}.firmova-recovery";
var json=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {
 OlusturmaTarihi=DateTime.UtcNow,Cariler=new[]{new{Unvan="Synthetic legacy",Ad="Synthetic legacy",Tip=1,OlusturmaTarihi=DateTime.UtcNow}},
 Stoklar=Array.Empty<object>(),Faturalar=Array.Empty<object>(),KasaHareketleri=Array.Empty<object>(),Cekler=Array.Empty<object>()});
var before=await State();
var missingConfirm=await Result(await Post(stranger,"SifreliDosyaGeriYukle",new(){["restoreFormat"]="zip"},zip));
Check(missingConfirm.Html.Contains("Geri yükleme onayı"),"ZIP restore requires server-side confirmation");
var forged=await Result(await Post(stranger,"SifreliDosyaGeriYukle",new(){["restoreFormat"]="zip",["confirmRestore"]="true"},json,"looks-like.zip"));
Check(forged.Html.Contains("geçerli bir ZIP yedeği değil"),"extension alone never makes JSON a ZIP; no automatic legacy import");
var noFallback=await Result(await Post(stranger,"SifreliDosyaGeriYukle",new(){["restoreFormat"]="encrypted",["confirmRestore"]="true"},zip,archiveName));
Check(noFallback.Html.Contains("Şifreli arşiv doğrulanamadı"),"failed encrypted authentication never falls back to otherwise-valid ZIP");
var wrongRoute=await Result(await Post(stranger,"SifreliDosyaGeriYukle",new(){["restoreFormat"]="zip",["confirmRestore"]="true"},encrypted,"looks-like.zip"));
Check(wrongRoute.Html.Contains("geçerli bir ZIP yedeği değil"),"encrypted bytes cannot bypass authentication through ZIP route");
var invalidType=await Result(await Post(stranger,"SifreliDosyaGeriYukle",new(){["restoreFormat"]="json",["confirmRestore"]="true"},json));
Check(invalidType.Html.Contains("Desteklenen bir yedek türü"),"main restore workflow does not expose legacy importer");
var forgedZip=await Result(await Post(stranger,"SifreliDosyaGeriYukle",new(){["restoreFormat"]="zip",["confirmRestore"]="true"},new byte[]{0x50,0x4b,3,4,1,2,3,4}));
Check(forgedZip.Html.Contains("ZIP yedeği doğrulanamadı"),"ZIP signature is only routing precheck; full validator still rejects corrupt package");
var missingCsrf=await Result(await Post(owner,"SifreliDosyaGeriYukle",new(){["restoreFormat"]="zip",["confirmRestore"]="true"},zip,csrf:false));
Check(missingCsrf.Status==HttpStatusCode.BadRequest,"CSRF protection remains active");
var legacyWithout=await Result(await Post(stranger,"GeriYukle",null,json,"legacy.json","YedekDosyasi"));
Check(legacyWithout.Html.Contains("Ayrı aktarım onayı gerekiyor"),"legacy JSON cannot run without its own explicit consent");
Check(await State()==before,"all rejected actions preserve synthetic records");

var restoredZip=await Result(await Post(stranger,"SifreliDosyaGeriYukle",new(){["restoreFormat"]="zip",["confirmRestore"]="true"},zip));
Check(restoredZip.Html.Contains("Tam yedek geri yüklendi:"),"single-input ZIP restore uses existing service on empty target");
var occupied=await State();
var replayZip=await Result(await Post(stranger,"SifreliDosyaGeriYukle",new(){["restoreFormat"]="zip",["confirmRestore"]="true"},zip));
Check(replayZip.Html.Contains("yalnızca boş muhasebe alanına") && await State()==occupied,"ZIP cannot overwrite or merge into occupied target");
var legacyWith=await Result(await Post(stranger,"GeriYukle",new(){["confirmLegacyImport"]="true"},json,"legacy.json","YedekDosyasi"));
Check(legacyWith.Html.Contains("Eski JSON yedeği yüklendi") && await db.CariKartlar.AnyAsync(x=>x.FirmaId==72 && x.Unvan=="Synthetic legacy"),"confirmed legacy import retains existing append behavior on nonempty target");
var foreign=await Result(await Post(stranger,"SifreliDosyaGeriYukle",new(){["confirmRestore"]="true"},encrypted,archiveName));
Check(foreign.Html.Contains("Şifreli arşiv doğrulanamadı"),"tenant-bound encrypted validation unchanged");
config["CompanyRecovery:Enabled"]="false";
var disabled=await owner.GetStringAsync(page);
Check(!disabled.Contains("value=\"encrypted\"") && disabled.Contains("value=\"zip\" checked=\"checked\""),"disabled encryption presents explicit ZIP choice without automatic create fallback");
var disabledRestore=await Result(await Post(owner,"SifreliDosyaGeriYukle",new(){["confirmRestore"]="true"},encrypted,archiveName));
Check(disabledRestore.Html.Contains("Şifreli arşiv doğrulanamadı"),"disabled encrypted upload cannot be used through unified UI route");
config["CompanyRecovery:Enabled"]="true";

// Empty only the synthetic source accounting area, preserving its company/users.
await db.EkDosyalar.Where(x=>x.FirmaId==71).ExecuteDeleteAsync();await db.Cekler.Where(x=>x.FirmaId==71).ExecuteDeleteAsync();
await db.Faturalar.Where(x=>x.FirmaId==71).ExecuteDeleteAsync();await db.CariKartlar.Where(x=>x.FirmaId==71).ExecuteDeleteAsync();
foreach(var path in Directory.GetFiles(Path.Combine(disk,"uploads/.firmova-private/recovery"),"accounting-*.firmova-recovery"))File.Delete(path);
var missingEncryptedConsent=await Result(await Post(owner,"SifreliDosyaGeriYukle",new(),encrypted,archiveName));
Check(missingEncryptedConsent.Html.Contains("Geri yükleme onayı"),"encrypted restore still requires consent on empty target");
var restored=await Result(await Post(owner,"SifreliDosyaGeriYukle",new(){["confirmRestore"]="true"},encrypted,archiveName));
Check(restored.Html.Contains("Şifreli arşiv geri yüklendi."),"default encrypted workflow restores from downloaded copy without server archive");
var cheque=await db.Cekler.AsNoTracking().SingleAsync(x=>x.FirmaId==71);var attachment=await db.EkDosyalar.AsNoTracking().SingleAsync(x=>x.FirmaId==71);
Check((await owner.GetByteArrayAsync("/belgeler/cek/"+cheque.Id)).SequenceEqual(image) && (await owner.GetByteArrayAsync("/belgeler/ek/"+attachment.Id)).SequenceEqual(pdf),"restored cheque and attachment remain readable through protected endpoints");
var encryptedReplay=await Result(await Post(owner,"SifreliDosyaGeriYukle",new(){["confirmRestore"]="true"},encrypted,archiveName));
Check(encryptedReplay.Html.Contains("yalnızca boş muhasebe alanına"),"encrypted overwrite restriction unchanged");
await app.StopAsync();Console.WriteLine($"PASS backup UI: {checks} targeted HTTP checks, synthetic PostgreSQL/files only.");
