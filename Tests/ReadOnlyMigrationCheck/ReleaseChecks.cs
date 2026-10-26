using System.Globalization;
using Npgsql;

static class ReleaseChecks
{
    internal const long Ceiling = 1_000_000_000;
    internal const long Reserve = 268_435_456;
    internal const long UploadThreshold = Ceiling - Reserve - 4 * 104_857_600L;
    internal const string Sql = """
        WITH objects AS (
          SELECT pg_catalog.to_regclass('public."PrivateMessages"') AS messages,
                 pg_catalog.to_regclass('public."__EFMigrationsHistory"') AS history,
                 pg_catalog.to_regclass('public."PrivateMediaContents"') AS contents,
                 pg_catalog.to_regclass('public."PrivateMediaQuotas"') AS quotas
        )
        SELECT pg_catalog.pg_database_size(pg_catalog.current_database()) AS database_bytes,
          pg_catalog.has_database_privilege(pg_catalog.current_database(),'CONNECT') AS db_connect,
          pg_catalog.has_schema_privilege('public','USAGE') AS schema_usage,
          pg_catalog.has_schema_privilege('public','CREATE') AS schema_create,
          (SELECT pg_catalog.has_language_privilege(oid,'USAGE') FROM pg_catalog.pg_language WHERE lanname='plpgsql') AS language_usage,
          pg_catalog.has_column_privilege(messages,'Id','REFERENCES') AS message_references,
          (pg_catalog.has_column_privilege(history,'MigrationId','SELECT') AND
           pg_catalog.has_column_privilege(history,'ProductVersion','SELECT')) AS history_select,
          (pg_catalog.has_column_privilege(history,'MigrationId','INSERT') AND
           pg_catalog.has_column_privilege(history,'ProductVersion','INSERT')) AS history_insert,
          pg_catalog.has_table_privilege(messages,'SELECT') AS messages_select,
          pg_catalog.has_table_privilege(messages,'INSERT') AS messages_insert,
          pg_catalog.has_table_privilege(messages,'UPDATE') AS messages_update,
          pg_catalog.has_table_privilege(messages,'DELETE') AS messages_delete,
          CASE WHEN messages IS NOT NULL THEN pg_catalog.has_sequence_privilege(
            pg_catalog.pg_get_serial_sequence('public."PrivateMessages"','Id'),'USAGE') END AS message_sequence,
          contents IS NOT NULL AS contents_exists, quotas IS NOT NULL AS quotas_exists,
          pg_catalog.has_table_privilege(contents,'SELECT') AS contents_select,
          pg_catalog.has_table_privilege(contents,'INSERT') AS contents_insert,
          pg_catalog.has_table_privilege(contents,'DELETE') AS contents_delete,
          pg_catalog.has_table_privilege(quotas,'SELECT') AS quota_select,
          CASE WHEN quotas IS NOT NULL THEN pg_catalog.has_column_privilege(quotas,'UsedBytes','UPDATE') END AS quota_update,
          (SELECT rolsuper FROM pg_catalog.pg_roles WHERE rolname=current_user) AS superuser,
          (SELECT rolbypassrls FROM pg_catalog.pg_roles WHERE rolname=current_user) AS bypass_rls,
          (SELECT rolcreaterole FROM pg_catalog.pg_roles WHERE rolname=current_user) AS create_role,
          (SELECT rolcreatedb FROM pg_catalog.pg_roles WHERE rolname=current_user) AS create_database,
          (SELECT rolreplication FROM pg_catalog.pg_roles WHERE rolname=current_user) AS replication,
          (SELECT relrowsecurity FROM pg_catalog.pg_class WHERE oid=messages) AS messages_rls,
          (SELECT relrowsecurity FROM pg_catalog.pg_class WHERE oid=history) AS history_rls,
          (SELECT count(*) FROM pg_catalog.pg_default_acl d
            WHERE d.defaclrole=(SELECT oid FROM pg_catalog.pg_roles WHERE rolname=current_user)
            AND (d.defaclnamespace=0 OR d.defaclnamespace=(SELECT oid FROM pg_catalog.pg_namespace WHERE nspname='public'))
          ) AS creator_default_acl_entries
        FROM objects
        """;
    internal static async Task<Dictionary<string,string?>> Read(NpgsqlConnection connection)
    {
        await using var cmd = new NpgsqlCommand(Sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) throw new InvalidOperationException();
        return Enumerable.Range(0,reader.FieldCount).ToDictionary(reader.GetName,
            i=> reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture));
    }
    static bool Yes(IReadOnlyDictionary<string,string?> d,string key) =>
        d.TryGetValue(key,out var value) && bool.TryParse(value,out var b) && b;
    internal static bool MigrationEligible(IReadOnlyDictionary<string,string?> d) =>
        new[]{"db_connect","schema_usage","schema_create","language_usage","message_references","history_select","history_insert"}.All(k=>Yes(d,k));
    internal static void Append(List<string> output, IReadOnlyDictionary<string,string?> d)
    {
        long bytes=long.Parse(d["database_bytes"]!,CultureInfo.InvariantCulture);
        output.Add($"Veritabani boyutu: {bytes} bayt ({bytes/1048576d:F2} MiB)");
        output.Add($"Kapasite politikasi: tavan={Ceiling}, rezerv={Reserve}, yukleme esigi={UploadThreshold} bayt");
        output.Add("Yeni yukleme kapasite on kosulu: "+(bytes<=UploadThreshold?"UYGUN":"YETERSIZ; yuklemeler kapali kalmali"));
        output.Add("Not: pg_database_size fiziksel disk/WAL boslugunu veya Render doluluk yuzdesini dogrulamaz.");
        output.Add("PrivateMediaContents: "+(Yes(d,"contents_exists")?"MEVCUT":"YOK"));
        output.Add("PrivateMediaQuotas: "+(Yes(d,"quotas_exists")?"MEVCUT":"YOK"));
        foreach(var key in new[]{"db_connect","schema_usage","schema_create","language_usage","message_references","history_select","history_insert"})
            output.Add("Migration yetkisi "+key+": "+(Yes(d,key)?"VAR":"YOK/BELIRSIZ"));
        output.Add("Mevcut rol migration yetki on kontrolu: "+(MigrationEligible(d)?"GEREKLI ON YETKILER VAR":"YETERSIZ/BELIRSIZ"));
        output.Add("Sinir: yeni nesneleri olusturacak rol sahip olur; trigger/index/ALTER kendi nesnelerinde mumkundur. DDL uygulanarak test edilmedi.");
        foreach(var key in new[]{"messages_select","messages_insert","messages_update","messages_delete","message_sequence"})
            output.Add("Runtime mevcut mesaj yetkisi "+key+": "+(Yes(d,key)?"VAR":"YOK/BELIRSIZ"));
        if(!Yes(d,"contents_exists") || !Yes(d,"quotas_exists")) {
            output.Add("Runtime yeni medya yetkileri: HENUZ DOGRULANAMAZ; tablolar yok/eksik.");
            output.Add("Olusturulduktan sonra gerekenler: contents SELECT/INSERT/DELETE; quotas SELECT ve UPDATE(UsedBytes).");
            output.Add("Ayni rol olusturursa sahiplik verir; ayri runtime rolu icin mevcut/default grant'ler varsayilamaz.");
        } else {
            foreach(var key in new[]{"contents_select","contents_insert","contents_delete","quota_select","quota_update"})
                output.Add("Runtime yeni medya yetkisi "+key+": "+(Yes(d,key)?"VAR":"YOK/BELIRSIZ"));
        }
        output.Add("Runtime invoker trigger'i kota SELECT/UPDATE gerektirir; bu yetki kotayi elle degistiren kimlik bilgisine karsi guvenlik siniri degildir.");
        foreach(var key in new[]{"superuser","bypass_rls","create_role","create_database","replication"})
            output.Add("Rol yuksek yetki "+key+": "+(Yes(d,key)?"VAR; runtime icin inceleyin":"YOK"));
        output.Add("RLS mesaj/history: "+(Yes(d,"messages_rls")||Yes(d,"history_rls")?"ACIK; politika uygunlugu ayrica incelenmeli":"KAPALI"));
        output.Add("Mevcut olusturucu rol icin ilgili default ACL kaydi: "+d["creator_default_acl_entries"]+" (gelecekteki ayri runtime yetkisi kaniti degil)");
        output.Add("Eski disk medyasi: dogrulanmis sayi/boyut/dosya varligi envanteri YOK. Onceki bulgu: otomatik aktarim ve disk fallback yok; okunabilirlik garanti degil.");
        output.Add("Medya icerigi/dosya sistemi veya mesaj satirlari okunmadi; yalnizca mevcut envanter belirsizligi raporlandi.");
    }
    internal static void SelfTest()
    {
        if(UploadThreshold!=312_134_144)throw new Exception();
        var values=new Dictionary<string,string?> { ["database_bytes"]="103400000",["creator_default_acl_entries"]="0" };
        foreach(var key in new[]{"db_connect","schema_usage","schema_create","language_usage","message_references","history_select","history_insert"})values[key]="True";
        if(!MigrationEligible(values))throw new Exception();
        foreach(var key in values.Keys.Where(k=>k!="database_bytes"&&k!="creator_default_acl_entries").ToArray()) {
            values[key]="False";if(MigrationEligible(values))throw new Exception();values[key]="True";
        }
        var lines=new List<string>();Append(lines,values);
        if(!lines.Any(x=>x.Contains("HENUZ DOGRULANAMAZ"))||!lines.Any(x=>x.Contains("UYGUN")))throw new Exception();
        values["database_bytes"]=(UploadThreshold+1).ToString(CultureInfo.InvariantCulture);
        lines.Clear();Append(lines,values);if(!lines.Any(x=>x.Contains("YETERSIZ;")))throw new Exception();
    }
    internal static async Task<int> Synthetic(string[] args)
    {
        // Explicit synthetic-only entry point. No supplied host/database/URL is accepted.
        if(args.Length is not (2 or 3) || !int.TryParse(args[1],out var port) || port<1024 || port>65535)throw new Exception();
        bool present=args.Length==3 && args[2]=="present";
        if(args.Length==3 && !present)throw new Exception();
        var password=await Console.In.ReadToEndAsync();
        if(!System.Text.RegularExpressions.Regex.IsMatch(password,"^[0-9A-F]{64}$"))throw new Exception();
        var b=new NpgsqlConnectionStringBuilder { Host="127.0.0.1",Port=port,Database="firmova_checker_synthetic",
            Username="checker_fixture",Password=password,SslMode=SslMode.Disable,Pooling=false,Timeout=5,CommandTimeout=10,
            IncludeErrorDetail=false,Options="-c default_transaction_read_only=on -c search_path=pg_catalog" };
        await using var c=new NpgsqlConnection(b.ConnectionString);b.Clear();password="";await c.OpenAsync();
        if(c.PostgreSqlVersion!=new Version(18,6))throw new Exception();
        await using var begin=new NpgsqlCommand("BEGIN READ ONLY",c);await begin.ExecuteNonQueryAsync();
        int checks=0;
        try {
            foreach(var role in new[]{"fixture_migrator","fixture_reader","fixture_runtime"}) {
                await using(var set=new NpgsqlCommand("SET LOCAL ROLE "+role,c))await set.ExecuteNonQueryAsync();
                var d=await Read(c);
                if(MigrationEligible(d)!=(role=="fixture_migrator"))throw new Exception();checks++;
                if(Yes(d,"contents_exists")!=present||Yes(d,"quotas_exists")!=present)throw new Exception();checks++;
                if(long.Parse(d["database_bytes"]!)<=0)throw new Exception();checks++;
                var lines=new List<string>();Append(lines,d);
                if(lines.Any(x=>x.Contains("HENUZ DOGRULANAMAZ"))==present)throw new Exception();checks++;
                if(Yes(d,"messages_select")!=(role!="fixture_reader"))throw new Exception();checks++;
                await using var mode=new NpgsqlCommand("SHOW transaction_read_only",c);
                if((string?)await mode.ExecuteScalarAsync()!="on")throw new Exception();checks++;
                if(present) {
                    foreach(var key in new[]{"contents_select","contents_insert","contents_delete","quota_select","quota_update"}) {
                        if(Yes(d,key)!=(role=="fixture_runtime"))throw new Exception();checks++;
                    }
                }
            }
        } finally { await using var rollback=new NpgsqlCommand("ROLLBACK",c);await rollback.ExecuteNonQueryAsync(); }
        // The fixture has TLS disabled. VerifyFull must reject it, never downgrade.
        var strict=new NpgsqlConnectionStringBuilder(c.ConnectionString) { SslMode=SslMode.VerifyFull, GssEncryptionMode=GssEncryptionMode.Disable };
        bool rejected=false;
        try {await using var tls=new NpgsqlConnection(strict.ConnectionString);await tls.OpenAsync();}
        catch(NpgsqlException) {rejected=true;}
        if(!rejected)throw new Exception();checks++;
        Console.WriteLine($"PASS: {checks} synthetic PostgreSQL 18.6 READ ONLY size/role/table/TLS-rejection checks. No live connection; synthetic transport only.");return 0;
    }
}
