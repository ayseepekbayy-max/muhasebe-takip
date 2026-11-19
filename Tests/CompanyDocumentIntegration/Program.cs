using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

if (!OperatingSystem.IsLinux() || args is not ["uploads"])
    throw new InvalidOperationException("Run only in the isolated fixture.");

const string disk = "/work/documents";
var dbConnection = "Host=/work;Database=firmova_stage10_synthetic;Username=postgres";
var write = true;
const bool privateEnabled = true;
Directory.CreateDirectory(disk);
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dbConnection).Options;
await using var db = new AppDbContext(options);
var helperBody=await File.ReadAllTextAsync("/sql/CompanyDocumentFileLocks.sql");
await db.Database.ExecuteSqlRawAsync(helperBody.Replace("{","{{").Replace("}","}}"));
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
builder.Services.AddScoped<AppDbContext>(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dbConnection+";Options=-c role=firmova_app_runtime").Options));

await using var app = builder.Build();
app.UseAccountingPublicAssets();
app.UseRouting();
app.UseAccountingRecoveryAdmission();
app.UseSession();
app.Use(CompanySessionValidation.InvokeAsync);
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

Check(await db.Set<CompanyStoredFile>().CountAsync(x=>x.OwnerCompanyId==71)==3,"new cheque/invoice/cari uploads have persistent owners");
Check(await db.Set<CompanyStoredFileReference>().CountAsync(x=>x.CompanyId==71)==3,"all three document references are tracked");
var cheque=await db.Cekler.SingleAsync();
Check((await owner.GetByteArrayAsync("/belgeler/cek/"+cheque.Id)).SequenceEqual(image),"tracked cheque retains protected HTTP endpoint");
foreach(var attachment in await db.EkDosyalar.ToListAsync())
 Check((await owner.GetByteArrayAsync("/belgeler/ek/"+attachment.Id)).SequenceEqual(pdf),"tracked attachment retains protected HTTP endpoint");
Check(await db.Set<CompanyStoredFile>().AllAsync(x=>x.RetiredAtUtc==null && x.Length>0 && x.Sha256.Length==64),"new ownership proofs contain hashes and lengths");
var sharedCompany=await db.Firmalar.SingleAsync(x=>x.Id==72);
sharedCompany.LogoYolu=cheque.ResimYolu!.Replace(".png","%2epng");
await db.SaveChangesAsync();
using(var pageResponse=await owner.GetAsync("/Cekler"))
{
    var html=await pageResponse.Content.ReadAsStringAsync();
    var fields=new Dictionary<string,string>{["id"]=cheque.Id.ToString()};
    foreach(var field in new[]{"__RequestVerificationToken","AccountingCompanyId","AccountingVersion"})
    {
        var match=Regex.Match(html,"name=\""+field+"\"[^>]*value=\"([^\"]+)\"");
        if(match.Success)fields[field]=WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    using var deleted=await owner.PostAsync("/Cekler?handler=Sil",new FormUrlEncodedContent(fields));
    Check(deleted.StatusCode==HttpStatusCode.Redirect,"tracked individual cheque deletion remains functional");
}
Check(!await db.Cekler.AnyAsync() && await db.Set<CompanyStoredFileReference>().CountAsync()==2,"cheque reference removed without touching attachments");
await using(var cleanupDb=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dbConnection+";Options=-c role=firmova_app_runtime").Options))
    await CompanyFileCleanupWorker.DrainAsync(cleanupDb,new CompanyDocumentStore(cleanupDb,app.Environment),default);
Check(!await db.Set<CompanyFileCleanupJob>().AnyAsync(x=>x.CompletedAtUtc!=null),"normal cheque cleanup preserves an existing other-company encoded reference");
sharedCompany.LogoYolu="";await db.SaveChangesAsync();
await using(var cleanupDb=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(dbConnection+";Options=-c role=firmova_app_runtime").Options))
    await CompanyFileCleanupWorker.DrainAsync(cleanupDb,new CompanyDocumentStore(cleanupDb,app.Environment),default);
Check(await db.Set<CompanyFileCleanupJob>().AnyAsync(x=>x.CompletedAtUtc!=null),"normal cheque cleanup completes through restricted role");
await app.StopAsync();Console.WriteLine("PASS restricted-role real cheque invoice and cari upload integration");
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
