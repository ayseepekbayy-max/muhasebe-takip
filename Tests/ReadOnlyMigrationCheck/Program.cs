using Npgsql;
using System.Text.Json;
using System.Text.RegularExpressions;

return await Check.Run(args);

static class Check
{
    const string Baseline = "20261020103737_AddCashBankAccounts";
    const string Target = "20261024104204_AddPostgresPrivateMedia";
    // Added in c93bc68 / 257a7ae / 6e50889; removed together in bb021df.
    // Exempt only these verified historical IDs, never arbitrary unknown rows.
    static readonly HashSet<string> Historical = new(StringComparer.Ordinal) {
        "20260407122817_InitialCreate",
        "20260413121958_CalisanArsivEklendi",
        "20260413125140_CalisanPasifArsivSistemi"
    };
    static HashSet<string> LocalFiles(string directory) => Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileNameWithoutExtension)
        .Where(x => x != null && Regex.IsMatch(x, @"^\d{14}_[A-Za-z0-9_]+$"))
        .Select(x => x!).ToHashSet(StringComparer.Ordinal);
    static string[] Unexplained(IEnumerable<string> applied, HashSet<string> local) =>
        applied.Except(local).Except(Historical).Order().ToArray();
    internal static NpgsqlConnectionStringBuilder Parse(string input)
    {
        var u = new Uri(input.Trim(), UriKind.Absolute);
        if (u.Scheme is not ("postgres" or "postgresql") ||
            (u.Query.Length != 0 && !Regex.IsMatch(u.Query, @"^\?sslmode=(disable|allow|prefer|require|verify-ca|verify-full)$")) || u.Fragment.Length != 0)
            throw new ArgumentException();
        var user = u.UserInfo.Split(':', 2);
        if (user.Length != 2 || string.IsNullOrEmpty(u.Host) || u.AbsolutePath.Length < 2) throw new ArgumentException();
        return new NpgsqlConnectionStringBuilder {
            Host = u.Host, Port = u.Port > 0 ? u.Port : 5432,
            Database = Uri.UnescapeDataString(u.AbsolutePath[1..]),
            Username = Uri.UnescapeDataString(user[0]), Password = Uri.UnescapeDataString(user[1]),
            SslMode = SslMode.VerifyFull, GssEncryptionMode = GssEncryptionMode.Disable, Pooling = false, Timeout = 10, CommandTimeout = 15,
            IncludeErrorDetail = false, LogParameters = false, PersistSecurityInfo = false,
            ApplicationName = "firmova_readonly_migration_check",
            Options = "-c default_transaction_read_only=on -c statement_timeout=15000 -c lock_timeout=3000 -c search_path=pg_catalog"
        };
    }
    public static async Task<int> Run(string[] args)
    {
        try {
            if(args.Length>0 && args[0]=="--synthetic-check") return await ReleaseChecks.Synthetic(args);
            if (args.SequenceEqual(new[]{"--self-test"})) {
                ReleaseChecks.SelfTest();
                var b = Parse("postgresql://synthetic:p%40ss@localhost/synthetic");
                if (b.Password != "p@ss" || b.SslMode != SslMode.VerifyFull || b.Pooling || !(b.Options?.Contains("read_only=on") ?? false)) throw new Exception();
                if (Parse("postgresql://synthetic:synthetic@localhost/synthetic?sslmode=disable").SslMode != SslMode.VerifyFull) throw new Exception();
                foreach(var bad in new[]{"invalid", "http://a:b@localhost/db", "postgresql://a:b@localhost/db?options=unsafe"}) {
                    bool rejected = false; try { Parse(bad); } catch { rejected = true; }
                    if (!rejected) throw new Exception();
                }
                var syntheticLocal = new HashSet<string>{Baseline, Target};
                var applied = Historical.Append(Baseline).ToArray();
                if (Unexplained(applied, syntheticLocal).Length != 0) throw new Exception();
                if (Unexplained(applied.Append("20990101000000_Unknown"), syntheticLocal).Length != 1) throw new Exception();
                if (Unexplained(new[]{"20260407122817_InitialCreateTypo"}, syntheticLocal).Length != 1) throw new Exception();
                syntheticLocal.Add("20260901000000_Required");
                if (!syntheticLocal.Where(x => string.CompareOrdinal(x,Target)<0).Except(applied).SequenceEqual(new[]{"20260901000000_Required"})) throw new Exception();
                var temp = Path.Combine(Path.GetTempPath(), "firmova-check-" + Guid.NewGuid().ToString("N"));
                try {
                    Directory.CreateDirectory(Path.Combine(temp,"Sqlite"));
                    File.WriteAllText(Path.Combine(temp, Baseline+".cs"), "// synthetic");
                    File.WriteAllText(Path.Combine(temp, Baseline+".Designer.cs"), "// synthetic");
                    File.WriteAllText(Path.Combine(temp, "AppDbContextModelSnapshot.cs"), "// synthetic");
                    File.WriteAllText(Path.Combine(temp,"Sqlite","20260304075801_InitialCreate.cs"), "// synthetic");
                    if (!LocalFiles(temp).SetEquals(new[]{Baseline})) throw new Exception();
                } finally { if (Directory.Exists(temp)) Directory.Delete(temp,true); }
                Console.WriteLine("SELF-TEST OK (no network)"); return 0;
            }
            if (args.Length != 0 || !Console.IsInputRedirected) throw new Exception();
            var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../Migrations"));
            var local = LocalFiles(directory);
            if (!local.Contains(Target) || !local.Contains(Baseline)) throw new Exception();
            var input = await Console.In.ReadToEndAsync();
            var builder = Parse(input); input = "";
            await using var conn = new NpgsqlConnection(builder.ConnectionString); builder.Clear();
            await conn.OpenAsync();
            // Session default is read-only before BEGIN; BEGIN also explicitly enforces it.
            await using (var begin = new NpgsqlCommand("BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY", conn)) await begin.ExecuteNonQueryAsync();
            var output = new List<string>();
            async Task<List<string[]>> Rows(string sql) {
                await using var cmd = new NpgsqlCommand(sql, conn);
                await using var r = await cmd.ExecuteReaderAsync();
                var rows = new List<string[]>();
                while (await r.ReadAsync()) rows.Add(Enumerable.Range(0, r.FieldCount).Select(i => r.GetValue(i).ToString() ?? "").ToArray());
                return rows;
            }
            try {
                if ((await Rows("SHOW transaction_read_only"))[0][0] != "on") throw new Exception();
                var tls=await Rows("SELECT ssl, version FROM pg_catalog.pg_stat_ssl WHERE pid=pg_catalog.pg_backend_pid()");
                if(tls.Count!=1 || !bool.Parse(tls[0][0]))throw new Exception();
                output.Add("Baglanti: OK | TLS: VerifyFull | READ ONLY: on");
                output.Add("TLS protokolu: "+tls[0][1]+" | sertifika/hostname Npgsql VerifyFull tarafindan dogrulandi.");
                ReleaseChecks.Append(output,await ReleaseChecks.Read(conn));
                var history = await Rows("""
                    SELECT n.nspname FROM pg_catalog.pg_class c
                    JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                    WHERE c.relname='__EFMigrationsHistory' AND c.relkind IN ('r','p')
                    AND n.nspname NOT IN ('pg_catalog','information_schema')
                    """);
                if (history.Count != 1 || history[0][0] != "public") throw new Exception();
                var ids = (await Rows("SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\""))
                    .Select(r => r[0]).ToHashSet();
                output.Add("Migration kimlikleri:");
                output.AddRange(ids.Order().Select(x => "  " + JsonSerializer.Serialize(x)));
                output.Add("AddCashBankAccounts: " + (ids.Contains(Baseline) ? "UYGULANMIS" : "YOK"));
                output.Add("AddPostgresPrivateMedia: " + (ids.Contains(Target) ? "UYGULANMIS" : "YOK"));
                var cols = await Rows("""
                    SELECT a.attname, pg_catalog.format_type(a.atttypid,a.atttypmod), a.attnotnull
                    FROM pg_catalog.pg_attribute a JOIN pg_catalog.pg_class c ON c.oid=a.attrelid
                    JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='public' AND c.relname='PrivateMessages' AND c.relkind IN ('r','p')
                    AND a.attnum>0 AND NOT a.attisdropped
                    """);
                bool columns = new[]{("Id","integer",true),("Kind","integer",true),
                    ("MediaKey","character varying(32)",false),("CreatedAtUtc","timestamp with time zone",true)}
                    .All(x => cols.Any(c => c[0]==x.Item1 && c[1]==x.Item2 && bool.Parse(c[2])==x.Item3));
                var pk = await Rows("""
                    SELECT k.conname FROM pg_catalog.pg_constraint k JOIN pg_catalog.pg_class c ON c.oid=k.conrelid
                    JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                    JOIN pg_catalog.pg_attribute a ON a.attrelid=c.oid AND a.attname='Id'
                    WHERE n.nspname='public' AND c.relname='PrivateMessages' AND k.contype='p'
                    AND k.conkey=ARRAY[a.attnum]::smallint[]
                    """);
                var conflicts = await Rows("""
                    SELECT c.relname FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='public' AND c.relname IN ('PrivateMediaContents','PrivateMediaQuotas',
                    'PrivateMediaQuotas_Id_seq','PK_PrivateMediaContents','PK_PrivateMediaQuotas',
                    'IX_PrivateMediaContents_ExpiresAtUtc','IX_PrivateMediaContents_MediaKey')
                    UNION ALL SELECT p.proname FROM pg_catalog.pg_proc p JOIN pg_catalog.pg_namespace n ON n.oid=p.pronamespace
                    WHERE n.nspname='public' AND p.proname IN ('firmova_private_media_quota','firmova_private_media_no_truncate')
                    UNION ALL SELECT t.typname FROM pg_catalog.pg_type t JOIN pg_catalog.pg_namespace n ON n.oid=t.typnamespace
                    WHERE n.nspname='public' AND t.typname IN ('PrivateMediaContents','PrivateMediaQuotas')
                    UNION ALL SELECT t.tgname FROM pg_catalog.pg_trigger t JOIN pg_catalog.pg_class c ON c.oid=t.tgrelid
                    JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public'
                    AND c.relname='PrivateMediaContents' AND t.tgname IN ('firmova_private_media_quota_trigger','firmova_private_media_no_truncate_trigger')
                    """);
                bool language = (await Rows("SELECT lanname FROM pg_catalog.pg_language WHERE lanname='plpgsql'")).Count==1;
                var missing = local.Where(x => string.CompareOrdinal(x,Target)<0 && !ids.Contains(x)).Order().ToArray();
                var unknown = ids.Except(local).Order().ToArray();
                var historical = unknown.Where(Historical.Contains).ToArray();
                var unexplained = Unexplained(ids, local);
                output.Add("PrivateMessages kolonlari / Id primary key: " + (columns && pk.Count==1 ? "OK" : "INCELEME GEREKLI"));
                output.Add("Yeni migration nesneleri: " + (conflicts.Count==0 ? "YOK" : "MEVCUT; inceleme gerekli"));
                output.Add("plpgsql: " + (language ? "OK" : "YOK"));
                output.Add("Eksik onceki migration sayisi: " + missing.Length);
                output.AddRange(missing.Select(x => "  " + JsonSerializer.Serialize(x)));
                output.Add("Yerel zincirde olmayan migration sayisi: " + unknown.Length);
                output.AddRange(unknown.Select(x => "  " + JsonSerializer.Serialize(x)));
                output.Add("Git gecmisinde dogrulanmis eski migration sayisi: " + historical.Length);
                output.Add("Aciklanamayan migration sayisi: " + unexplained.Length);
                bool ready=ids.Contains(Baseline) && missing.Length==0 && unexplained.Length==0 && columns && pk.Count==1 && conflicts.Count==0 && language;
                output.Add("On degerlendirme: " + (ids.Contains(Target) ? "ZATEN UYGULANMIS; sema butunlugu ayrica incelenmeli." : ready ? "Kontrol edilen sema on kosullari UYUMLU." : "INCELEME GEREKLI; migration uygulanmamali."));
            } finally {
                await using var rollback = new NpgsqlCommand("ROLLBACK", conn); await rollback.ExecuteNonQueryAsync();
            }
            await conn.CloseAsync();
            output.Add("Islem: ROLLBACK | Baglanti kapatildi.");
            output.Add("Kapsam: READ ONLY katalog, DB boyutu ve migration gecmisi; gercek DDL/DML, fiziksel disk boslugu ve medya icerigi test edilmedi.");
            Console.WriteLine(string.Join(Environment.NewLine,output)); return 0;
        } catch {
            Console.WriteLine("KONTROL DURDU: Baglanti veya kontrol hatasi; ayrintilar gizlendi."); return 1;
        }
    }
}
