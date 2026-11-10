using System.Data.Common;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
var ct = timeout.Token;
var checks = 0;
void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks++; Console.WriteLine("PASS " + label); }
var bytes = "%PDF-1.4 synthetic document"u8.ToArray();

foreach (var invoice in new[] { true, false })
foreach (var mode in new[] { "success", "write-failure", "commit-before", "commit-after", "foreign", "forged-company", "inactive", "deleted-user" })
{
    await using var fixture = await Fixture.CreateAsync(ct);
    await using (var seed = new AppDbContext(fixture.Options))
    {
        seed.Faturalar.AddRange(new Fatura { Id = 501, FirmaId = 71, CariKartId = 201, FaturaNo = "Synthetic A" }, new Fatura { Id = 502, FirmaId = 72, CariKartId = 202, FaturaNo = "Synthetic B" });
        if (mode == "inactive") (await seed.Firmalar.SingleAsync(x => x.Id == 71, ct)).AktifMi = false;
        if (mode == "deleted-user") seed.Kullanicilar.Remove(await seed.Kullanicilar.SingleAsync(x => x.Id == 101, ct));
        await seed.SaveChangesAsync(ct);
    }
    fixture.Fault.Mode = mode;
    await using var db = new AppDbContext(fixture.Options);
    var http = new DefaultHttpContext { Session = new SyntheticSession(), RequestAborted = ct };
    http.Session.SetInt32("FirmaId", 71);
    http.Session.SetInt32("KullaniciId", mode == "forged-company" ? 102 : 101);
    using var input = new MemoryStream(bytes);
    var file = new FormFile(input, 0, bytes.Length, "EkDosya", "synthetic.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
    var temp = new TempDataDictionary(http, new SyntheticTempData());
    IActionResult? result = null;
    Exception? error = null;
    var target = mode == "foreign" ? (invoice ? 502 : 202) : (invoice ? 501 : 201);
    try
    {
        if (invoice)
        {
            var page = new MuhasebeTakip2.App.Pages.Faturalar.DetayModel(db, fixture.Env, new IslemGecmisiService(db, new HttpContextAccessor { HttpContext = http })) { PageContext = new PageContext { HttpContext = http }, TempData = temp, EkDosya = file };
            result = await page.OnPostDosyaEkleAsync(target).WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        else
        {
            var page = new MuhasebeTakip2.App.Pages.CariKartlar.EkstreModel(db, fixture.Env) { PageContext = new PageContext { HttpContext = http }, TempData = temp, EkDosya = file };
            result = await page.OnPostDosyaEkleAsync(target).WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
    }
    catch (IOException ex) { error = ex; }
    var prefix = (invoice ? "invoice " : "cari ") + mode;
    var faulted = mode is "write-failure" or "commit-before" or "commit-after";
    Check((error != null) == faulted, prefix + " error outcome");
    if (faulted) Check(fixture.Fault.Reached, prefix + " fault reached");
    if (mode == "success") Check(result is RedirectToPageResult, prefix + " original redirect");
    if (mode is "forged-company" or "inactive" or "deleted-user") Check(result is StatusCodeResult { StatusCode: 403 }, prefix + " identity denied");
    if (mode == "foreign") Check(result is NotFoundResult, prefix + " foreign record denied");
    await using var verify = new AppDbContext(fixture.Options);
    var committed = mode is "success" or "commit-after";
    Check(await verify.EkDosyalar.CountAsync(ct) == (committed ? 1 : 0), prefix + " database state");
    var files = Directory.GetFiles(fixture.Root, "*.pdf", SearchOption.AllDirectories);
    Check(files.Length == (mode is "success" or "commit-before" or "commit-after" ? 1 : 0), prefix + " file compensation");
    if (files.Length > 0) Check((await File.ReadAllBytesAsync(files.Single(), ct)).SequenceEqual(bytes), prefix + " exact bytes");
    Check(!await verify.IslemGecmisleri.AnyAsync(ct), prefix + " baseline upload audit unchanged");
}

foreach (var mode in new[] { "success", "commit-before", "commit-after", "bad-hash" })
{
    await using var fixture = await Fixture.CreateAsync(ct);
    await using var db = new AppDbContext(fixture.Options);
    // Firm 72 must be empty for restore. Source has one card and one protected document.
    db.CariKartlar.Remove(await db.CariKartlar.SingleAsync(x => x.Id == 202, ct));
    var folder = Path.Combine(fixture.Root, "uploads", "cariler");
    Directory.CreateDirectory(folder);
    var name = "cari-201-00000000000000000000000000000001.pdf";
    await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, ct);
    db.EkDosyalar.Add(new EkDosya { FirmaId = 71, CariKartId = 201, DosyaYolu = "/uploads/cariler/" + name, DosyaAdi = "Synthetic.pdf", Boyut = bytes.Length });
    await db.SaveChangesAsync(ct);
    var backup = new AccountingBackupService(db, fixture.Env);
    var zip = await backup.ExportAsync(71, ct);
    if (mode == "bad-hash")
    {
        using var stream = new MemoryStream(); await stream.WriteAsync(zip, ct); stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            var entry = archive.Entries.First(x => x.FullName.StartsWith("files/"));
            var entryName = entry.FullName; entry.Delete(); using var output = archive.CreateEntry(entryName).Open(); output.Write(bytes.Select(x => (byte)(x ^ 1)).ToArray());
        }
        zip = stream.ToArray();
    }
    var stageRoot = Path.Combine(fixture.Root, "uploads", ".restore-staging");
    var unrelated = Directory.CreateDirectory(Path.Combine(stageRoot, "unrelated-synthetic")).FullName;
    await File.WriteAllTextAsync(Path.Combine(unrelated, "keep.txt"), "synthetic", ct);
    fixture.Fault.Mode = mode;
    var failed = false;
    try { using var input = new MemoryStream(zip); await backup.RestoreAsync(input, 72, ct); }
    catch (Exception ex) when (ex is AccountingBackupService.BackupException or IOException) { failed = true; }
    Check(failed == (mode != "success"), "restore " + mode + " result");
    await using var verify = new AppDbContext(fixture.Options);
    var committed = mode is "success" or "commit-after";
    Check(await verify.CariKartlar.CountAsync(x => x.FirmaId == 72, ct) == (committed ? 1 : 0), "restore " + mode + " target rows");
    Check(await verify.CariKartlar.CountAsync(x => x.FirmaId == 71, ct) == 1, "restore " + mode + " source preserved");
    var finalRoot = Path.Combine(fixture.Root, "uploads", "yedek");
    var finalFiles = Directory.Exists(finalRoot) ? Directory.GetFiles(finalRoot, "*", SearchOption.AllDirectories) : [];
    Check(finalFiles.Length == (mode == "bad-hash" ? 0 : 1), "restore " + mode + " ambiguous commit keeps files");
    if (finalFiles.Length > 0) Check((await File.ReadAllBytesAsync(finalFiles.Single(), ct)).SequenceEqual(bytes), "restore " + mode + " SHA validated bytes");
    Check(Directory.GetDirectories(stageRoot).SequenceEqual(new[] { unrelated }), "restore " + mode + " cleans only own staging");
    Check(!Directory.Exists(Path.Combine(fixture.Root, "App_Data", "RestoreStaging")), "restore " + mode + " no cross-filesystem staging");
}

await using (var fixture = await Fixture.CreateAsync(ct))
{
    var settings = new ConfigurationBuilder().AddInMemoryCollection().Build();
    foreach (var key in new[] { "Database:AutoMigrate", "Maintenance:AttachLegacyDataToFirstFirm", "Maintenance:FixDefaultAdminOnStartup" })
    {
        settings[key] = "true";
        var rejected = false; try { ReleaseMaintenance.ValidateStartup(settings, new SyntheticEnvironment(fixture.Root) { EnvironmentName = "Production" }); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Production startup blocks " + key); settings[key] = "false";
    }
    ReleaseMaintenance.ValidateStartup(settings, new SyntheticEnvironment(fixture.Root) { EnvironmentName = "Production" });
    Check(true, "Production safe startup accepted");
    Check(!new ReleaseMaintenance(settings, fixture.Env).Snapshot().Closed, "maintenance defaults open");
    settings["Maintenance:Enabled"] = "true";
    Check(new ReleaseMaintenance(settings, fixture.Env).Snapshot() is { Closed: true, Drained: true }, "startup maintenance without control file is closed");

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(AppDbContext).Assembly.GetName().Name, ContentRootPath = fixture.Root, WebRootPath = fixture.Root, EnvironmentName = "Testing", Args = [] });
    builder.Configuration.Sources.Clear();
    builder.Configuration.AddInMemoryCollection();
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Services.AddSingleton<ReleaseMaintenance>();
    await using var app = builder.Build();
    app.UseAccountingPublicAssets();
    app.UseReleaseMaintenance();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var writes = 0;
    app.MapGet("/dynamic", () => Results.Ok("normal"));
    app.MapGet("/Panel", () => Results.Ok("synthetic panel"));
    async Task<IResult> WriteSyntheticAsync()
    {
        await using var db = new AppDbContext(fixture.Options);
        db.Cekler.Add(new Cek { FirmaId = 71, No = "Synthetic admitted " + writes, Tarih = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        Interlocked.Increment(ref writes);
        return Results.Ok();
    }
    app.MapPost("/api/write", WriteSyntheticAsync);
    app.MapPost("/api/slow", async () => { entered.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(10)); return await WriteSyntheticAsync(); });
    await app.StartAsync(ct);
    try
    {
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(15) };
        var stageProbe = Path.Combine(fixture.Root, "uploads", ".restore-staging", "00000000000000000000000000000001");
        Directory.CreateDirectory(stageProbe);
        await File.WriteAllBytesAsync(Path.Combine(stageProbe,"00000000000000000000000000000002.pdf"), bytes, ct);
        using var stageResponse = await client.GetAsync("/uploads/.restore-staging/00000000000000000000000000000001/00000000000000000000000000000002.pdf", ct);
        Check(stageResponse.StatusCode == HttpStatusCode.NotFound, "physical restore staging is not statically served");
        using var encodedStage = await client.GetAsync("/uploads/%2erestore-staging/00000000000000000000000000000001/00000000000000000000000000000002.pdf", ct);
        Check(encodedStage.StatusCode == HttpStatusCode.NotFound, "encoded restore staging is not served");
        var pending = client.PostAsync("/api/slow", null, ct);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        var control = Path.Combine(fixture.Root, "App_Data", "release-maintenance.control");
        Directory.CreateDirectory(Path.GetDirectoryName(control)!);
        async Task Set(string text) { await File.WriteAllTextAsync(control + ".new", text, ct); File.Move(control + ".new", control, true); }
        await Set("closed");
        using var state = JsonDocument.Parse(await client.GetStringAsync("/health/live", ct));
        Check(state.RootElement.GetProperty("maintenance").GetBoolean() && !state.RootElement.GetProperty("drained").GetBoolean(), "maintenance waits for admitted write");
        foreach (var request in new[] { new HttpRequestMessage(HttpMethod.Get, "/dynamic"), new HttpRequestMessage(HttpMethod.Get, "/Panel"), new HttpRequestMessage(HttpMethod.Post, "/api/write"), new HttpRequestMessage(HttpMethod.Delete, "/api/write"), new HttpRequestMessage(HttpMethod.Post, "/health/live"), new HttpRequestMessage(HttpMethod.Get, "/health/live/extra") })
        {
            using var response = await client.SendAsync(request, ct); Check(response.StatusCode == HttpStatusCode.ServiceUnavailable, "maintenance blocks " + request.Method + " " + request.RequestUri);
        }
        await using var verifyWrites = new AppDbContext(fixture.Options);
        Check(writes == 0 && !await verifyWrites.Cekler.AnyAsync(ct), "blocked requests do not mutate database");
        release.TrySetResult();
        using var completed = await pending; Check(completed.IsSuccessStatusCode && writes == 1 && await verifyWrites.Cekler.CountAsync(ct) == 1, "admitted write completes exactly once");
        using var drained = JsonDocument.Parse(await client.GetStringAsync("/health/live", ct));
        Check(drained.RootElement.GetProperty("drained").GetBoolean(), "drained only after request completes");
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/health/live"), ct); Check(head.IsSuccessStatusCode && (await head.Content.ReadAsByteArrayAsync(ct)).Length == 0, "read-only health HEAD allowed");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "css")); await File.WriteAllTextAsync(Path.Combine(fixture.Root, "css", "site.css"), "body{}", ct);
        Check((await client.GetAsync("/css/site.css", ct)).IsSuccessStatusCode, "public static assets work in maintenance");
        await Set("open");
        Check((await client.GetAsync("/dynamic", ct)).IsSuccessStatusCode && (await client.GetAsync("/Panel", ct)).IsSuccessStatusCode, "normal and Panel access restored");
        Check((await client.PostAsync("/api/write", null, ct)).IsSuccessStatusCode && writes == 2 && await verifyWrites.Cekler.CountAsync(ct) == 2, "writes resume after explicit reopen");
        await Set("invalid"); Check((await client.GetAsync("/dynamic", ct)).StatusCode == HttpStatusCode.ServiceUnavailable, "invalid maintenance control fails closed");
        Check(!new AppDbContext(fixture.Options).Model.GetEntityTypes().Any(x => x.ClrType.Name == "AdminCompanyTransition"), "baseline schema has no portal transition table");
    }
    finally { release.TrySetResult(); await app.StopAsync(ct); }
}
Console.WriteLine($"PASS {checks} independent release safety checks; FAIL 0");

sealed class Fault : DbTransactionInterceptor, ISaveChangesInterceptor
{
    public string Mode { get; set; } = "";
    public bool Reached { get; private set; }
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    { if (Mode == "write-failure") { Reached = true; throw new IOException("Synthetic write failure"); } return ValueTask.FromResult(result); }
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    { if (Mode == "commit-before") { Reached = true; throw new IOException("Synthetic commit request failure"); } return ValueTask.FromResult(result); }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    { if (Mode == "commit-after") { Reached = true; throw new IOException("Synthetic lost commit acknowledgement"); } return Task.CompletedTask; }
}
sealed class Fixture : IAsyncDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("firmova-release-synthetic-").FullName;
    public SyntheticEnvironment Env => new(Root);
    public SqliteConnection Connection { get; } = new("Data Source=:memory:");
    public Fault Fault { get; } = new();
    public DbContextOptions<AppDbContext> Options => new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).AddInterceptors(Fault).Options;
    public static async Task<Fixture> CreateAsync(CancellationToken ct)
    {
        var f = new Fixture(); await f.Connection.OpenAsync(ct); await using var db = new AppDbContext(f.Options); await db.Database.EnsureCreatedAsync(ct);
        db.Firmalar.AddRange(new Firma { Id = 71, FirmaAdi = "Synthetic A" }, new Firma { Id = 72, FirmaAdi = "Synthetic B" });
        db.Kullanicilar.AddRange(new Kullanici { Id = 101, FirmaId = 71, KullaniciAdi = "Synthetic A" }, new Kullanici { Id = 102, FirmaId = 72, KullaniciAdi = "Synthetic B" });
        db.CariKartlar.AddRange(new CariKart { Id = 201, FirmaId = 71, Unvan = "Synthetic A" }, new CariKart { Id = 202, FirmaId = 72, Unvan = "Synthetic B" });
        await db.SaveChangesAsync(ct); return f;
    }
    public async ValueTask DisposeAsync() { await Connection.DisposeAsync(); Directory.Delete(Root, true); }
}