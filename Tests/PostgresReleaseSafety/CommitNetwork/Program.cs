using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;
using Npgsql;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var ct = deadline.Token;
var checks = 0;
void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks++; Console.WriteLine("PASS " + label); }
var bytes = "%PDF-1.4 synthetic network loss document"u8.ToArray();
foreach (var operation in new[] { "invoice", "cari", "cheque", "restore" })
{
    var root = Directory.CreateTempSubdirectory("firmova-network211-synthetic-").FullName;
    try
    {
        var database = "synthetic_211_network_" + Guid.NewGuid().ToString("N");
        var direct = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = 5432, Username = "postgres", Database = "postgres", SslMode = SslMode.Disable, GssEncryptionMode = GssEncryptionMode.Disable, Pooling = false, Timeout = 5, CommandTimeout = 10 };
        await using (var admin = new NpgsqlConnection(direct.ConnectionString))
        {
            await admin.OpenAsync(ct);
            Check(admin.PostgreSqlVersion.Major == 18, operation + " isolated PostgreSQL 18");
            await using var command = new NpgsqlCommand("CREATE DATABASE " + database, admin); await command.ExecuteNonQueryAsync(ct);
        }
        direct.Database = database;
        await using var proxy = new CommitReplyLossProxy();
        var proxied = new NpgsqlConnectionStringBuilder(direct.ConnectionString) { Port = proxy.Port };
        var arm = new ArmCommit(proxy);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(proxied.ConnectionString).AddInterceptors(arm).Options;
        var verifyOptions = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(direct.ConnectionString).Options;
        var env = new SyntheticEnvironment(root);
        var original = Path.Combine(root, "uploads", "cekler", "00000000-0000-0000-0000-000000000031.png");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!); await File.WriteAllBytesAsync(original, bytes, ct);
        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync(ct); // Fresh synthetic schema only; no migrations.
            seed.Firmalar.AddRange(new Firma { Id = 71, FirmaAdi = "Synthetic source" }, new Firma { Id = 72, FirmaAdi = "Synthetic target" });
            seed.Kullanicilar.Add(new Kullanici { Id = 101, FirmaId = 71, KullaniciAdi = "Synthetic user" });
            seed.CariKartlar.Add(new CariKart { Id = 201, FirmaId = 71, Unvan = "Synthetic card", OlusturmaTarihi = DateTime.UtcNow });
            seed.Faturalar.Add(new Fatura { Id = 501, FirmaId = 71, CariKartId = 201, FaturaNo = "Synthetic invoice" });
            seed.Cekler.Add(new Cek { Id = 31, FirmaId = 71, No = "Synthetic cheque", Tarih = DateTime.UtcNow, ResimYolu = "/uploads/cekler/00000000-0000-0000-0000-000000000031.png" });
            await seed.SaveChangesAsync(ct);
        }
        await using var db = new AppDbContext(options);
        var http = new DefaultHttpContext { Session = new SyntheticSession(), RequestAborted = ct };
        http.Session.SetInt32("FirmaId", 71); http.Session.SetInt32("KullaniciId", 101);
        var temp = new TempDataDictionary(http, new SyntheticTempData());
        byte[]? archive = null;
        if (operation == "restore") archive = await new AccountingBackupService(db, env).ExportAsync(71, ct);
        using var input = new MemoryStream(bytes);
        var file = new FormFile(input, 0, bytes.Length, "EkDosya", "synthetic.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        Exception? error = null;
        IActionResult? result = null;
        string? chequeError = null;
        arm.Enabled = true;
        try
        {
            if (operation == "invoice")
            {
                var page = new MuhasebeTakip2.App.Pages.Faturalar.DetayModel(db, env, new IslemGecmisiService(db, new HttpContextAccessor { HttpContext = http })) { PageContext = new PageContext { HttpContext = http }, TempData = temp, EkDosya = file };
                result = await page.OnPostDosyaEkleAsync(501).WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
            else if (operation == "cari")
            {
                var page = new MuhasebeTakip2.App.Pages.CariKartlar.EkstreModel(db, env) { PageContext = new PageContext { HttpContext = http }, TempData = temp, EkDosya = file };
                result = await page.OnPostDosyaEkleAsync(201).WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
            else if (operation == "cheque")
            {
                var page = new MuhasebeTakip2.App.Pages.Cekler.IndexModel(db, env, new UnusedStatusService()) { PageContext = new PageContext { HttpContext = http }, TempData = temp };
                result = await page.OnPostSilAsync(31).WaitAsync(TimeSpan.FromSeconds(10), ct); chequeError = page.Hata;
            }
            else
            {
                using var zip = new MemoryStream(archive!);
                await new AccountingBackupService(db, env).RestoreAsync(zip, 72, ct);
            }
        }
        catch (Exception ex) when (ex is NpgsqlException or AccountingBackupService.BackupException) { error = ex; }
        await proxy.ServerCommitObserved.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Check(arm.Reached && proxy.ServerCommitObserved.Task.IsCompletedSuccessfully, operation + " real COMMIT completion observed and TCP reply dropped");
        await using var verify = new AppDbContext(verifyOptions);
        if (operation is "invoice" or "cari")
        {
            Check(error is NpgsqlException && result == null, operation + " transport failure is not reported as success");
            var stored = await verify.EkDosyalar.AsNoTracking().SingleAsync(ct);
            Check(stored.FirmaId == 71 && (operation == "invoice" ? stored.FaturaId == 501 : stored.CariKartId == 201), operation + " independently verified committed row and firm");
            var path = Path.Combine(root, stored.DosyaYolu.TrimStart('/'));
            Check(File.Exists(path) && (await File.ReadAllBytesAsync(path, ct)).SequenceEqual(bytes), operation + " committed document survives actual connection loss");
        }
        else if (operation == "cheque")
        {
            Check(error == null && result is PageResult && !string.IsNullOrWhiteSpace(chequeError), "cheque reports unconfirmed deletion safely");
            Check(!await verify.Cekler.AnyAsync(x => x.Id == 31, ct), "cheque deletion independently confirmed after TCP cut");
            Check(File.Exists(original) && (await File.ReadAllBytesAsync(original, ct)).SequenceEqual(bytes), "cheque document preserved after TCP cut");
        }
        else
        {
            Check(error is AccountingBackupService.BackupException, "restore reports unknown commit, not success");
            Check(await verify.Cekler.CountAsync(x => x.FirmaId == 72, ct) == 1 && await verify.Faturalar.CountAsync(x => x.FirmaId == 72, ct) == 1 && await verify.CariKartlar.CountAsync(x => x.FirmaId == 72, ct) == 1, "restore rows independently confirmed after TCP cut");
            var restored = await verify.Cekler.AsNoTracking().SingleAsync(x => x.FirmaId == 72, ct);
            var path = Path.Combine(root, restored.ResimYolu!.TrimStart('/'));
            Check(File.Exists(path) && (await File.ReadAllBytesAsync(path, ct)).SequenceEqual(bytes), "restore preserves committed document after TCP cut");
            var invoice = await verify.Faturalar.AsNoTracking().SingleAsync(x => x.FirmaId == 72, ct);
            Check(await verify.CariKartlar.AnyAsync(x => x.Id == invoice.CariKartId && x.FirmaId == 72, ct), "restore ID mapping and firm ownership survive TCP cut");
        }
        Check(!await verify.IslemGecmisleri.AnyAsync(ct), operation + " baseline audit behavior unchanged");
    }
    finally { Directory.Delete(root, true); }
}
Console.WriteLine($"PASS {checks} real PostgreSQL COMMIT TCP reply-loss checks; FAIL 0; BLOCKED 0");

sealed class ArmCommit(CommitReplyLossProxy proxy) : DbTransactionInterceptor
{
    public bool Enabled { get; set; }
    public bool Reached { get; private set; }
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (Enabled) { Enabled = false; Reached = true; proxy.Arm(); }
        return ValueTask.FromResult(result);
    }
}