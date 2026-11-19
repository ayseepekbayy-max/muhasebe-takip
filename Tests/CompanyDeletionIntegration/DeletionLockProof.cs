using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Hosting;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;
using Npgsql;

internal static class DeletionLockProof
{
    private const string Connection = "Host=127.0.0.1;Database=firmova_stage10_synthetic;Username=firmova_app_runtime;SSL Mode=VerifyFull;Root Certificate=/work/tls/ca.crt;Pooling=false;Command Timeout=25";
    private const string Admin = "Host=127.0.0.1;Database=firmova_stage10_synthetic;Username=postgres;SSL Mode=VerifyFull;Root Certificate=/work/tls/ca.crt;Pooling=false";
    private static AppDbContext Context(string name) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(Connection + ";Application Name=" + name).Options);
    private static async Task Sql(NpgsqlConnection connection, string sql)
    { await using var c = new NpgsqlCommand(sql, connection); await c.ExecuteNonQueryAsync(); }
    private static async Task<NpgsqlConnection> Open(string connection)
    { var c = new NpgsqlConnection(connection); await c.OpenAsync(); return c; }

    internal static async Task RunAsync()
    {
        if (!OperatingSystem.IsLinux()) throw new InvalidOperationException("Synthetic Linux fixture only.");
        await using var admin = await Open(Admin);
        await using var seed = Context("synthetic-seed");
        seed.Add(new Kullanici { Id = 103, FirmaId = 73, KullaniciAdi = "synthetic-delete" });
        seed.AddRange(new Fatura { Id = 901, FirmaId = 73, FaturaNo = "synthetic-target" },
            new Fatura { Id = 902, FirmaId = 74, FaturaNo = "synthetic-preserve" });
        await seed.SaveChangesAsync();
        Directory.CreateDirectory("/recovery/wwwroot/uploads/.firmova-private/objects");
        var sentinel = "/recovery/wwwroot/uploads/.firmova-private/objects/synthetic-preserved";
        await File.WriteAllTextAsync(sentinel, "synthetic bytes unchanged");
        var sentinelDigest = SHA256.HashData(await File.ReadAllBytesAsync(sentinel));
        var baseline = await Snapshot(admin);

        // Real deletion service: a different firm's existing write blocks acquisition.
        await using (var holder = await Open(Connection))
        {
            await Sql(holder, "BEGIN; UPDATE public.\"Faturalar\" SET \"FaturaNo\"=\"FaturaNo\" WHERE \"Id\"=902");
            var elapsed = Stopwatch.StartNew();
            var deleting = Attempt("deletion-acquire");
            await Wait(admin, "deletion-acquire", "Lock");
            var reader = Stopwatch.StartNew();
            await Sql(admin, "SELECT count(*) FROM public.\"Firmalar\"");
            Check(reader.Elapsed < TimeSpan.FromSeconds(2), "ordinary reads remain available");
            var writing = WriteOtherCompany();
            var error = await deleting;
            var writeMs = await writing;
            Check(error is PostgresException { SqlState: "55P03" }, "lock acquisition timed out with 55P03");
            Check(elapsed.Elapsed.TotalSeconds is >= 4.5 and < 9, "5-second acquisition bound");
            await Sql(holder, "ROLLBACK");
            await Unchanged(admin, baseline, sentinel, sentinelDigest);
            Console.WriteLine($"PASS acquisition timeout={elapsed.ElapsedMilliseconds}ms; other-company write wait={writeMs}ms; reads available; full rollback");
        }

        // Force a pause after children/users were deleted but before parent deletion completes.
        await InstallPause(admin, 20);
        {
            var elapsed = Stopwatch.StartNew();
            var deleting = Attempt("deletion-statement");
            await Wait(admin, "deletion-statement", "Timeout");
            var writing = WriteOtherCompany();
            var privateWriting = WritePrivatePanel();
            var error = await deleting;
            var writeMs = await writing;
            var privateMs = await privateWriting;
            Check(error is PostgresException { SqlState: "57014" }, "statement deadline cancelled partial deletion");
            Check(elapsed.Elapsed.TotalSeconds is >= 9 and < 14, "10-second statement bound");
            await Unchanged(admin, baseline, sentinel, sentinelDigest);
            Console.WriteLine($"PASS statement timeout={elapsed.ElapsedMilliseconds}ms; accounting wait={writeMs}ms; private-panel wait={privateMs}ms; children/users restored");
        }

        // Request cancellation while the SAME real service is paused mid-delete.
        {
            using var cancel = new CancellationTokenSource();
            var deleting = Attempt("deletion-cancelled", cancel.Token);
            await Wait(admin, "deletion-cancelled", "Timeout");
            var elapsed = Stopwatch.StartNew();
            cancel.Cancel();
            var error = await deleting;
            Check(error is OperationCanceledException or NpgsqlException, "cancelled request did not commit");
            var writeMs = await WriteOtherCompany();
            Check(elapsed.Elapsed < TimeSpan.FromSeconds(5), "cancelled request released its locks");
            await Unchanged(admin, baseline, sentinel, sentinelDigest);
            Console.WriteLine($"PASS interrupted deletion rollback={elapsed.ElapsedMilliseconds}ms; subsequent write={writeMs}ms; no partial data/file loss");
        }
        await RemovePause(admin);

        // Verify the exact production server policy independently of the application timer:
        // both commands are individually <10s but their total exceeds 15s.
        {
            await using var orphan = await Open(Connection);
            await Sql(orphan, "BEGIN; " + PostgreSqlCompanyDeletion.TransactionLimitsSql +
                "; LOCK TABLE public.\"Firmalar\" IN SHARE ROW EXCLUSIVE MODE; DELETE FROM public.\"Faturalar\" WHERE \"Id\"=901");
            var elapsed = Stopwatch.StartNew();
            var writing = WriteOtherCompany();
            Exception? error = null;
            try { await Sql(orphan, "SELECT pg_sleep(8)"); await Sql(orphan, "SELECT pg_sleep(8)"); }
            catch (NpgsqlException ex) { error = ex; }
            var writeMs = await writing;
            Check(error is not null && elapsed.Elapsed.TotalSeconds is >= 13 and < 19, "server total transaction deadline");
            await Unchanged(admin, baseline, sentinel, sentinelDigest);
            Console.WriteLine($"PASS server total deadline={elapsed.ElapsedMilliseconds}ms; writer resumed={writeMs}ms; partial delete rolled back without client timer");
        }
        // An application stuck outside a SQL command cannot retain locks indefinitely either.
        {
            await using var idle = await Open(Connection);
            await Sql(idle, "BEGIN; " + PostgreSqlCompanyDeletion.TransactionLimitsSql +
                "; LOCK TABLE public.\"Firmalar\" IN SHARE ROW EXCLUSIVE MODE; DELETE FROM public.\"Faturalar\" WHERE \"Id\"=901");
            var elapsed = Stopwatch.StartNew();
            var writeMs = await WriteOtherCompany();
            Check(elapsed.Elapsed.TotalSeconds is >= 4 and < 9, "idle transaction released at 5s");
            await Unchanged(admin, baseline, sentinel, sentinelDigest);
            Console.WriteLine($"PASS idle-application session deadline={writeMs}ms; locks released and partial delete rolled back");
        }

        await using (var runtime = Context("protected-targets"))
        {
            foreach (var id in new[] { 1, 72 })
            {
                try
                {
                    await new PostgreSqlCompanyDeletion(runtime).DeleteCompanyAsync(id, 72, 101,
                        Guid.NewGuid(), new CompanyDocumentStore(runtime, new Environment()), default);
                    throw new InvalidDataException("Protected target accepted.");
                }
                catch (ArgumentOutOfRangeException) when (id == 1) { }
                catch (InvalidOperationException) when (id == 72) { }
            }
        }
        await Unchanged(admin, baseline, sentinel, sentinelDigest);
        var completed = await Attempt("deletion-success");
        Check(completed is null, "normal deletion still completes within budget");
        await using var check = Context("verify-success");
        Check(!await check.Firmalar.AnyAsync(x => x.Id == 73) && !await check.Kullanicilar.AnyAsync(x => x.Id == 103) &&
            await check.Firmalar.CountAsync() == 3 && await check.Faturalar.AnyAsync(x => x.Id == 902 && x.FirmaId == 74 && x.FaturaNo == "synthetic-preserve"),
            "successful deletion only removed selected synthetic company");
        var finalDigest = SHA256.HashData(await File.ReadAllBytesAsync(sentinel));
        Check(sentinelDigest.SequenceEqual(finalDigest), "unrelated file unchanged");
        Console.WriteLine("PASS protected targets and independent records/files; normal bounded service completes; no automatic retry");
    }

    private static async Task<Exception?> Attempt(string name, CancellationToken ct = default)
    {
        await using var db = Context(name);
        try
        {
            await new PostgreSqlCompanyDeletion(db).DeleteCompanyAsync(73, 72, 101, Guid.NewGuid(),
                new CompanyDocumentStore(db, new Environment()), ct);
            return null;
        }
        catch (Exception ex) when (ex is NpgsqlException or OperationCanceledException) { return ex; }
    }
    private static async Task<long> WriteOtherCompany()
    {
        await using var c = await Open(Connection);
        var elapsed = Stopwatch.StartNew();
        await Sql(c, "BEGIN; UPDATE public.\"Firmalar\" SET \"FirmaAdi\"='synthetic temporary write' WHERE \"Id\"=74; ROLLBACK");
        return elapsed.ElapsedMilliseconds;
    }
    private static async Task<long> WritePrivatePanel()
    {
        await using var db = Context("private-writer");
        await using var tx = await db.Database.BeginTransactionAsync();
        db.PrivateMessages.Add(new PrivateMessage { SenderPerson = 1, Content = "synthetic temporary write" });
        var elapsed = Stopwatch.StartNew();
        await db.SaveChangesAsync();
        await tx.RollbackAsync();
        return elapsed.ElapsedMilliseconds;
    }
    private static async Task Wait(NpgsqlConnection admin, string name, string waitType)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(4))
        {
            await using var c = new NpgsqlCommand("SELECT EXISTS(SELECT FROM pg_stat_activity WHERE application_name=@name AND wait_event_type=@type)", admin);
            c.Parameters.AddWithValue("name", name); c.Parameters.AddWithValue("type", waitType);
            if ((bool)(await c.ExecuteScalarAsync())!) return;
            await Task.Delay(20);
        }
        throw new InvalidDataException("Expected isolated deletion wait was not reached.");
    }
    private static async Task InstallPause(NpgsqlConnection admin, int seconds)
    {
        await Sql(admin, $"""
            CREATE FUNCTION public.synthetic_pause_delete() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF OLD."Id"=73 THEN PERFORM pg_sleep({seconds}); END IF; RETURN OLD; END; $$;
            CREATE TRIGGER zz_synthetic_pause BEFORE DELETE ON public."Firmalar"
            FOR EACH ROW EXECUTE FUNCTION public.synthetic_pause_delete();
            """);
    }
    private static Task RemovePause(NpgsqlConnection admin) => Sql(admin,
        "DROP TRIGGER zz_synthetic_pause ON public.\"Firmalar\"; DROP FUNCTION public.synthetic_pause_delete()");
    private static async Task<Dictionary<string,string>> Snapshot(NpgsqlConnection admin)
    {
        var result = new Dictionary<string,string>();
        var tables = new List<string>();
        await using (var c = new NpgsqlCommand("SELECT quote_ident(c.relname) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND c.relkind='r' ORDER BY c.relname", admin))
        await using (var r = await c.ExecuteReaderAsync()) { while (await r.ReadAsync()) tables.Add(r.GetString(0)); }
        foreach (var table in tables)
        {
            await using var c = new NpgsqlCommand($"SELECT md5(coalesce(string_agg(to_jsonb(t)::text,chr(10) ORDER BY to_jsonb(t)::text),'')) FROM public.{table} t", admin);
            result[table] = (string)(await c.ExecuteScalarAsync())!;
        }
        return result;
    }
    private static async Task Unchanged(NpgsqlConnection admin, Dictionary<string,string> expected, string path, byte[] digest)
    {
        var actual = await Snapshot(admin);
        Check(actual.Count == expected.Count && expected.All(x => actual.GetValueOrDefault(x.Key) == x.Value), "all business/control rows preserved after failed deletion");
        var actualDigest = SHA256.HashData(await File.ReadAllBytesAsync(path));
        Check(digest.SequenceEqual(actualDigest), "file bytes preserved");
    }
    private static void Check(bool success, string description)
    { if (!success) throw new InvalidDataException(description); }
    private sealed class Environment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Synthetic";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = "/recovery";
        public string WebRootPath { get; set; } = "/recovery/wwwroot";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
