using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

var root = Path.Combine(Path.GetTempPath(), "firmova-documents-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try { await TestAsync(root); }
finally
{
    // Only the unique, newly created fixture root can be removed.
    if (Path.GetDirectoryName(Path.GetFullPath(root)) == Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) &&
        Path.GetFileName(root).StartsWith("firmova-documents-", StringComparison.Ordinal))
        Directory.Delete(root, recursive: true);
}

static async Task TestAsync(string root)
{
    await using var connection = new SqliteConnection("Data Source=:memory:");
    await connection.OpenAsync();
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
    await using var seed = new AppDbContext(options);
    await seed.Database.EnsureCreatedAsync();

    const string a = "/uploads/cariler/cari-11-00000000000000000000000000000001.pdf";
    const string b = "/uploads/faturalar/fatura-22-00000000000000000000000000000002.pdf";
    const string zip = "/uploads/yedek/00000000000000000000000000000003/00000000000000000000000000000004.docx";
    const string cheque = "/uploads/cekler/00000000-0000-0000-0000-000000000005.png";
    const string privateCheque = "/uploads/.firmova-private/objects/cekler/00000000-0000-0000-0000-000000000032.png";
    const string privateAttachment = "/uploads/.firmova-private/objects/cariler/cari-11-00000000000000000000000000000011.pdf";
    const string wrongOwner = "/uploads/faturalar/fatura-23-00000000000000000000000000000008.pdf";
    const string orphan = "/uploads/yedek/00000000000000000000000000000003/00000000000000000000000000000010.pdf";
    const string duplicate = "/uploads/yedek/00000000000000000000000000000006/00000000000000000000000000000007.pdf";

    void Write(string path, string contents)
    {
        var file = Path.Combine(root, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, contents);
    }
    Write(a, "synthetic-cari-A"); Write(b, "synthetic-invoice-B"); Write(zip, "synthetic-restored-A");
    Write(wrongOwner, "synthetic-wrong-owner"); Write(orphan, "synthetic-orphan");
    Write(cheque, "synthetic-cheque-A"); Write(duplicate, "synthetic-ambiguous");
    Write(privateCheque, "synthetic-private-cheque"); Write(privateAttachment, "synthetic-private-attachment");
    Write("/css/site.css", "body{}"); Write("/js/site.js", "void 0;"); Write("/images/logo.png", "public-image");
    Write("/favicon.ico", "public-icon"); Write("/MuhasebeTakip2.App.styles.css", "body{}");

    seed.Firmalar.AddRange(new Firma { Id = 71, FirmaAdi = "Synthetic A" },
        new Firma { Id = 72, FirmaAdi = "Synthetic B" }, new Firma { Id = 73, FirmaAdi = "Synthetic inactive", AktifMi = false });
    seed.Kullanicilar.AddRange(new Kullanici { Id = 101, FirmaId = 71, KullaniciAdi = "Synthetic A" },
        new Kullanici { Id = 102, FirmaId = 72, KullaniciAdi = "Synthetic B" },
        new Kullanici { Id = 103, FirmaId = 73, KullaniciAdi = "Synthetic inactive" });
    seed.CariKartlar.AddRange(new CariKart { Id = 11, FirmaId = 71, Unvan = "Synthetic A" },
        new CariKart { Id = 12, FirmaId = 72, Unvan = "Synthetic B" });
    seed.Faturalar.Add(new Fatura { Id = 22, FirmaId = 72, CariKartId = 12, FaturaNo = "Synthetic B" });
    seed.Faturalar.Add(new Fatura { Id = 23, FirmaId = 72, CariKartId = 12, FaturaNo = "Synthetic invalid relation" });
    seed.Cekler.Add(new Cek { Tip = CekTipi.Alinacak, Id = 31, FirmaId = 71, No = "Synthetic cheque", ResimYolu = cheque });
    seed.Cekler.Add(new Cek { Tip = CekTipi.Alinacak, Id = 32, FirmaId = 71, No = "Synthetic private cheque", ResimYolu = privateCheque });
    seed.EkDosyalar.AddRange(
        new EkDosya { Id = 1, FirmaId = 71, CariKartId = 11, DosyaYolu = a, DosyaAdi = "Ekstre.pdf", IcerikTipi = "text/html" },
        new EkDosya { Id = 2, FirmaId = 72, FaturaId = 22, DosyaYolu = b },
        new EkDosya { Id = 3, FirmaId = 71, CariKartId = 11, DosyaYolu = zip, DosyaAdi = "Arşiv.docx" },
        new EkDosya { Id = 4, FirmaId = 71, FaturaId = 23, DosyaYolu = wrongOwner },
        new EkDosya { Id = 5, FirmaId = 71, CariKartId = 11, DosyaYolu = "/uploads/cariler/../../css/site.css" },
        new EkDosya { Id = 6, FirmaId = 71, CariKartId = 11, DosyaYolu = "/uploads/cariler/%2e%2e/site.css" },
        new EkDosya { Id = 7, FirmaId = 71, DosyaYolu = orphan },
        new EkDosya { Id = 8, FirmaId = 71, CariKartId = 11, DosyaYolu = duplicate },
        new EkDosya { Id = 9, FirmaId = 72, CariKartId = 12, DosyaYolu = duplicate },
        new EkDosya { Id = 10, FirmaId = 71, CariKartId = 11, DosyaYolu = "/uploads/cariler/cari-11-00000000000000000000000000000009.pdf" },
        new EkDosya { Id = 11, FirmaId = 71, CariKartId = 11, DosyaYolu = privateAttachment });
    var now = DateTime.UtcNow;
    var mediaKey = Guid.NewGuid().ToString("N");
    seed.PrivateMessages.Add(new PrivateMessage { Id = 51, SenderPerson = 1, SenderName = "Synthetic private",
        Kind = PrivateMessageKind.Audio, MediaKey = mediaKey, MediaContentType = "audio/webm", CreatedAtUtc = now });
    seed.PrivateMediaContents.Add(new PrivateMediaContent { MessageId = 51, MediaKey = mediaKey,
        Payload = "synthetic-private-audio"u8.ToArray(), CreatedAtUtc = now, ExpiresAtUtc = now.AddHours(24) });
    await seed.SaveChangesAsync();

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        ApplicationName = typeof(AppDbContext).Assembly.GetName().Name, WebRootPath = root, EnvironmentName = "Testing", Args = []
    });
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddInMemoryCollection();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSession();
    builder.Services.AddRazorPages().AddMvcOptions(options => options.Filters.Add(new SwitchedAccountingBoundary.HandlerFilter()));
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<IIslemGecmisiService, IslemGecmisiService>();
    builder.Services.AddScoped<IOdemeBildirimService, OdemeBildirimService>();
    builder.Services.AddScoped<ICekDurumService, CekDurumService>();
    builder.Services.AddScoped<AppDbContext>(_ => new AppDbContext(options));
    builder.Services.AddAdminPortalAuthorization();
    builder.Services.AddAccountingContextFoundation();


    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<PrivateLoginAttemptLimiter>();
    builder.Services.AddScoped<PrivateAccessService>();
    builder.Services.AddScoped<PrivateMediaStore>();

    await using var app = builder.Build();
    app.UseAccountingPublicAssets();
    app.UseRouting();
    app.UseSession();
    app.UseSwitchedAccountingBoundary();
    app.MapAccountingDocuments();
    app.MapRazorPages();
    // The only identity fixture. No production startup, credentials or configuration are loaded.
    app.MapGet("/fixture/{user:int}/{company:int}", async (int user, int company, HttpContext http, AccountingSessionLifecycle lifecycle) =>
    {
        await lifecycle.ReplaceAsync(http, () =>
        {
            http.Session.Clear();
            http.Session.SetInt32("KullaniciId", user);
            http.Session.SetInt32("FirmaId", company);
            return Task.CompletedTask;
        }, establishAccounting: true);
        return Results.Ok();
    });
    app.MapGet("/fixture/private", async (HttpContext http, AccountingSessionLifecycle lifecycle) =>
    {
        await lifecycle.ReplaceAsync(http, () =>
        {
            http.Session.Clear();
            http.Session.SetString("PrivateMode", "1");
            http.Session.SetString("PrivatePerson", "1");
            return Task.CompletedTask;
        });
        return Results.Ok();
    });
    await app.StartAsync();
    try
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
        int checks = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException(label);
            checks++;
            Console.WriteLine("PASS " + label);
        }
        async Task Denied(string path)
        {
            using var response = await client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Check(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden &&
                !body.Contains(root) && !body.Contains("synthetic-"), "denied " + path);
        }
        foreach (var asset in new[] { "/css/site.css", "/js/site.js", "/images/logo.png", "/favicon.ico", "/MuhasebeTakip2.App.styles.css" })
            Check((await client.GetAsync(asset)).IsSuccessStatusCode, "public asset " + asset);
        await Denied(a);
        await Denied(zip);
        await Denied("/belgeler/ek/1");
        Check((await client.GetAsync("/fixture/101/71")).IsSuccessStatusCode, "firm A identity established");
        Check(await client.GetStringAsync("/belgeler/cek/32") == "synthetic-private-cheque", "private cheque uses existing endpoint");
        Check(await client.GetStringAsync("/belgeler/ek/11") == "synthetic-private-attachment", "private attachment uses existing endpoint");
        await Denied(privateCheque);
        await Denied(privateAttachment);
        var archiveId = Guid.NewGuid();
        var archive = CompanyRecoveryArchive.OnCompanyDisk(app.Environment, new byte[32], "synthetic");
        archive.Save(new(71, archiveId, new(), [new(archiveId, "synthetic-recovery-payload"u8.ToArray())]));
        Check(archive.Read(71, archiveId).Documents.Single().Content.AsSpan().SequenceEqual("synthetic-recovery-payload"u8), "encrypted recovery round trip");
        var (archiveName, _) = archive.ReadEncryptedDownload(71, archiveId);
        await Denied("/uploads/.firmova-private/recovery/" + archiveName);
        foreach (var pagePath in new[] { "/Cekler", "/Cekler/Detay/31", "/CariKartlar/Ekstre/11" })
        {
            using var pageResponse = await client.GetAsync(pagePath);
            var html = await pageResponse.Content.ReadAsStringAsync();
            Check(pageResponse.IsSuccessStatusCode && html.Contains("/belgeler/") && !html.Contains("href=\"/uploads/"), "real view uses protected document links " + pagePath);
        }
        using (var response = await client.GetAsync("/belgeler/ek/1"))
            Check(response.IsSuccessStatusCode && await response.Content.ReadAsStringAsync() == "synthetic-cari-A" &&
                response.Content.Headers.ContentType?.MediaType == "application/pdf" &&
                response.Content.Headers.ContentDisposition?.DispositionType == "inline" &&
                response.Headers.CacheControl?.NoStore == true, "own PDF inline, safe type and no-store");
        using (var head = new HttpRequestMessage(HttpMethod.Head, "/belgeler/ek/1"))
        using (var response = await client.SendAsync(head))
            Check(response.StatusCode == HttpStatusCode.OK && (await response.Content.ReadAsByteArrayAsync()).Length == 0,
                "authorized HEAD has no file body");
        using (var range = new HttpRequestMessage(HttpMethod.Get, "/belgeler/ek/1"))
        {
            range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 8);
            using var response = await client.SendAsync(range);
            Check(response.StatusCode == HttpStatusCode.PartialContent &&
                await response.Content.ReadAsStringAsync() == "synthetic", "authorized range request");
        }
        using (var response = await client.GetAsync("/belgeler/ek/1?indir=1"))
            Check(response.IsSuccessStatusCode && response.Content.Headers.ContentDisposition?.DispositionType == "attachment",
                "explicit download");
        using (var response = await client.GetAsync("/belgeler/ek/3"))
            Check(await response.Content.ReadAsStringAsync() == "synthetic-restored-A" &&
                response.Content.Headers.ContentDisposition?.DispositionType == "attachment", "restored office document");
        using (var response = await client.GetAsync("/belgeler/cek/31"))
            Check(await response.Content.ReadAsStringAsync() == "synthetic-cheque-A" &&
                response.Content.Headers.ContentDisposition?.DispositionType == "inline", "own cheque image");
        foreach (var path in new[] { a, cheque, b, "/belgeler/ek/2", "/belgeler/ek/4", "/belgeler/ek/5",
            "/belgeler/ek/6", "/belgeler/ek/7", "/belgeler/ek/8", "/belgeler/ek/10", "/belgeler/ek/999",
            "/belgeler/cek/999", "/css/%2e%2e" + a, "/uploads./cariler/" + Path.GetFileName(a) })
            await Denied(path);
        Check((await client.GetAsync("/Panel?handler=Audio&id=51")).StatusCode == HttpStatusCode.Unauthorized,
            "ERP session does not grant private audio");
        Check((await client.GetAsync("/Logout")).StatusCode == HttpStatusCode.Redirect, "real logout handler");
        await Denied("/belgeler/ek/1");
        await client.GetAsync("/fixture/102/72");
        Check(await client.GetStringAsync("/belgeler/ek/2") == "synthetic-invoice-B", "firm B can read its invoice");
        using (var pageResponse = await client.GetAsync("/Faturalar/Detay/22"))
        {
            var html = await pageResponse.Content.ReadAsStringAsync();
            Check(pageResponse.IsSuccessStatusCode && html.Contains("/belgeler/ek/2") && !html.Contains("href=\"/uploads/"), "real invoice view uses protected link");
        }
        await Denied("/belgeler/ek/1");
        await Denied("/belgeler/cek/31");
        await Denied("/belgeler/cek/32");
        await Denied("/belgeler/ek/11");
        await client.GetAsync("/fixture/103/73");
        await Denied("/belgeler/ek/1");
        await client.GetAsync("/fixture/999/71");
        await Denied("/belgeler/ek/1");
        await client.GetAsync("/fixture/101/999");
        await Denied("/belgeler/ek/1");
        await client.GetAsync("/fixture/101/71");
        await seed.Firmalar.Where(x => x.Id == 71).ExecuteUpdateAsync(x => x.SetProperty(f => f.AktifMi, false));
        await Denied("/belgeler/ek/1");
        using var privateClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
        Check((await privateClient.GetAsync("/fixture/private")).IsSuccessStatusCode, "private session established");
        Check((await privateClient.GetAsync("/belgeler/ek/1")).StatusCode == HttpStatusCode.NotFound, "private session cannot read accounting attachment");
        Check(await privateClient.GetStringAsync("/Panel?handler=Audio&id=51") == "synthetic-private-audio",
            "actual private audio handler retains authorized access");
        Console.WriteLine($"PASS {checks} isolated document HTTP checks");
    }
    finally { await app.StopAsync(); }
}
