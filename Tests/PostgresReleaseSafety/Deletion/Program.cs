using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Pages.Cekler;
using MuhasebeTakip2.App.Services;

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
var ct = deadline.Token;
var checks = 0;
void Check(bool ok, string label)
{
    if (!ok) throw new InvalidOperationException(label);
    checks++;
    Console.WriteLine("PASS " + label);
}

foreach (var scenario in new[] { "success", "save-failure", "commit-before", "commit-rejected", "commit-after", "cleanup-failure", "foreign", "shared-path", "traversal", "no-session", "no-document" })
{
    // Every scenario has a fresh in-memory database and newly created synthetic files.
    var root = Directory.CreateTempSubdirectory("firmova-cheque-delete-").FullName;
    try
    {
        await using var connection = new Npgsql.NpgsqlConnection(PgFixture.NewDatabase());
        await connection.OpenAsync(ct);
        var fault = new DeleteFault();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).AddInterceptors(fault).Options;
        await using (var seed = new AppDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync(ct);
            await seed.Database.ExecuteSqlRawAsync(@"CREATE FUNCTION synthetic_commit_guard() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN IF current_setting('firmova.reject_commit', true) = 'on' THEN RAISE EXCEPTION 'synthetic_commit_rejected' USING ERRCODE='23514'; END IF; RETURN NULL; END $$;
CREATE CONSTRAINT TRIGGER synthetic_doc_guard AFTER INSERT ON ""EkDosyalar"" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION synthetic_commit_guard();
CREATE CONSTRAINT TRIGGER synthetic_cheque_guard AFTER DELETE ON ""Cekler"" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION synthetic_commit_guard();");
            seed.Firmalar.AddRange(new Firma { Id = 71, FirmaAdi = "Synthetic own" }, new Firma { Id = 72, FirmaAdi = "Synthetic other" });
            seed.Cekler.Add(new Cek { Tarih = DateTime.UtcNow, Id = 31, FirmaId = scenario == "foreign" ? 72 : 71, No = "Synthetic", ResimYolu = scenario == "no-document" ? null : "/uploads/cekler/00000000-0000-0000-0000-000000000031.png" });
            await seed.SaveChangesAsync(ct);
        }
        var path = Path.Combine(root, "uploads", "cekler", "00000000-0000-0000-0000-000000000031.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = "synthetic-cheque-document"u8.ToArray();
        if (scenario != "no-document") await File.WriteAllBytesAsync(path, bytes, ct);
        var outside = Path.Combine(root, "sentinel.txt");
        if (scenario is "shared-path" or "traversal")
        {
            await using var arrange = new AppDbContext(options);
            if (scenario == "shared-path")
                arrange.Cekler.Add(new Cek { Tarih = DateTime.UtcNow, Id = 32, FirmaId = 72, No = "Synthetic shared", ResimYolu = "/uploads/cekler/00000000-0000-0000-0000-000000000031.png" });
            else
            {
                (await arrange.Cekler.SingleAsync(ct)).ResimYolu = "/uploads/cekler/../../sentinel.txt";
                await File.WriteAllBytesAsync(outside, bytes, ct);
            }
            await arrange.SaveChangesAsync(ct);
        }
        fault.Mode = scenario;
        fault.Path = path;
        await using var db = new AppDbContext(options);
        var http = new DefaultHttpContext { Session = new SyntheticSession(), RequestAborted = ct };
        if (scenario != "no-session") http.Session.SetInt32("FirmaId", 71);
        var page = new IndexModel(db, new SyntheticEnvironment(root), new UnusedStatusService())
        {
            PageContext = new PageContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new SyntheticTempData())
        };
        var result = await page.OnPostSilAsync(31).WaitAsync(TimeSpan.FromSeconds(10), ct);
        await using var verify = new AppDbContext(options);
        var exists = await verify.Cekler.AnyAsync(x => x.Id == 31, ct);
        var failed = scenario is "save-failure" or "commit-before" or "commit-rejected" or "commit-after";
        Check(result is PageResult == failed, scenario + " response distinguishes unconfirmed deletion");
        Check(exists == (scenario is "save-failure" or "commit-before" or "commit-rejected" or "foreign" or "no-session"), scenario + " database state");
        if (scenario == "commit-rejected") Check(fault.ObservedSqlState == "23514", "real PostgreSQL COMMIT rejection");
        if (failed)
        {
            Check(fault.Reached, scenario + " injected fault reached");
            Check(!string.IsNullOrEmpty(page.Hata) && !page.Hata.Contains("SECRET") && !page.Hata.Contains(root), scenario + " safe error");
        }
        if (scenario == "cleanup-failure")
        {
            Check(fault.Reached && Directory.Exists(path), "cleanup failure injected after confirmed commit");
            Check((await File.ReadAllBytesAsync(path + ".held", ct)).SequenceEqual(bytes), "cleanup failure retains synthetic document bytes");
            Check(page.TempData["Hata"] is string warning && warning.Contains("Çek kaydı silindi") && !warning.Contains(root), "cleanup failure reports committed deletion without sensitive details");
        }
        else if (scenario is "success" or "no-document")
            Check(!File.Exists(path), scenario + " physical cleanup");
        else
            Check((await File.ReadAllBytesAsync(path, ct)).SequenceEqual(bytes), scenario + " document preserved");
        if (scenario == "shared-path") Check(await verify.Cekler.AnyAsync(x => x.Id == 32, ct), "other company's shared reference preserved");
        if (scenario == "traversal") Check((await File.ReadAllBytesAsync(outside, ct)).SequenceEqual(bytes), "path traversal cannot delete outside file");
        Check(!await verify.IslemGecmisleri.AnyAsync(ct), scenario + " existing no-audit deletion behavior unchanged");
    }
    finally
    {
        // Only this scenario's newly created synthetic directory is removed.
        Directory.Delete(root, recursive: true);
    }
}
Console.WriteLine($"PASS {checks} synthetic PostgreSQL cheque deletion checks; FAIL 0");

sealed class DeleteFault : DbTransactionInterceptor, ISaveChangesInterceptor
{
    public string? ObservedSqlState { get; private set; }
    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Exception is Npgsql.PostgresException pg) ObservedSqlState = pg.SqlState;
        return Task.CompletedTask;
    }
    public string Mode { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Reached { get; private set; }
    public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Mode == "save-failure") { Reached = true; throw new IOException("SECRET synthetic database write failure"); }
        return ValueTask.FromResult(result);
    }
    public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (Mode == "commit-rejected")
        {
            Reached = true;
            using var command = transaction.Connection!.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SET LOCAL firmova.reject_commit = 'on'"; command.ExecuteNonQuery();
            return ValueTask.FromResult(result);
        }
        if (Mode == "commit-before") { Reached = true; throw new IOException("SECRET synthetic failed commit request"); }
        return ValueTask.FromResult(result);
    }
    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (Mode == "commit-after") { Reached = true; throw new IOException("SECRET synthetic lost commit acknowledgement"); }
        if (Mode == "cleanup-failure")
        {
            // Deterministic, cross-platform obstruction to File.Delete, only in the fixture.
            Reached = true;
            File.Move(Path, Path + ".held");
            Directory.CreateDirectory(Path);
        }
        return Task.CompletedTask;
    }
}

sealed class SyntheticEnvironment(string root) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "ChequeDeletion";
    public string EnvironmentName { get; set; } = "Testing";
    public string ContentRootPath { get; set; } = root;
    public string WebRootPath { get; set; } = root;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
sealed class UnusedStatusService : ICekDurumService
{
    public Task<CekDurumSonucu> DurumDegistirAsync(int firmaId, int cekId, bool odendiMi, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("Deletion must not invoke cheque status updates.");
}
sealed class SyntheticTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
sealed class SyntheticSession : ISession
{
    private readonly Dictionary<string, byte[]> values = new();
    public bool IsAvailable => true;
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public IEnumerable<string> Keys => values.Keys;
    public void Clear() => values.Clear();
    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Remove(string key) => values.Remove(key);
    public void Set(string key, byte[] value) => values[key] = value;
    public bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value) => values.TryGetValue(key, out value);
}
static class PgFixture
{
    internal static string NewDatabase()
    {
        var name = "synthetic_211_" + Guid.NewGuid().ToString("N");
        using var admin = new Npgsql.NpgsqlConnection("Host=127.0.0.1;Username=postgres;Database=postgres;SSL Mode=Disable;GSS Encryption Mode=Disable;Pooling=false;Timeout=5;Command Timeout=10");
        admin.Open();
        if (admin.PostgreSqlVersion.Major != 18) throw new InvalidOperationException("Expected isolated PostgreSQL 18.");
        using var command = new Npgsql.NpgsqlCommand("CREATE DATABASE " + name, admin);
        command.ExecuteNonQuery();
        return "Host=127.0.0.1;Username=postgres;Database=" + name + ";SSL Mode=Disable;GSS Encryption Mode=Disable;Pooling=false;Timeout=5;Command Timeout=10";
    }
}
