using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using Npgsql;
using NpgsqlTypes;

namespace MuhasebeTakip2.App.Services;

// Internal recovery component. The caller must authorize the operation and coordinate document storage.
public sealed class PostgreSqlCompanyDeletion(AppDbContext db)
{
    internal const string TransactionLimitsSql = """
        SET LOCAL lock_timeout = '5s';
        SET LOCAL statement_timeout = '10s';
        SET LOCAL idle_in_transaction_session_timeout = '5s';
        SET LOCAL transaction_timeout = '15s';
        """;
    private sealed record DeletionSnapshot(Dictionary<string, JsonElement[]> Tables, Guid[] FileIds);
    private static readonly string[] Tables =
    [
        "Firmalar", "Kullanicilar", "Calisanlar", "CariKartlar", "Cekler", "FaturaNumaraAyarlari",
        "MaliyetKayitlari", "Musteriler", "OdemePlanlari", "StokUrunler", "CalisanAvanslari",
        "CalisanArsivleri", "KasaHesaplar", "KasaTransferler",
        "CalisanMaasArsivleri", "CalisanPuantajlari", "Faturalar", "FaturaKalemleri", "YoneticiNotlari",
        "MusteriIsler", "MusteriMasraflar", "OdemeBildirimGecmisleri", "OdemeBildirimGizlemeleri",
        "OdemeHareketleri", "StokHareketleri", "EkDosyalar", "KasaHareketleri", "IslemGecmisleri"
    ];
    // Existing production tables outside this checkout's EF migration chain.
    private static readonly Dictionary<string, string[]> ExistingColumns = new()
    {
        ["CalisanArsivleri"] = ["Id", "FirmaId", "EskiCalisanId", "AdSoyad", "Telefon", "Maas",
            "IseGirisTarihi", "AyrilisTarihi", "AyrilisNotu"],
        ["KasaHesaplar"] = ["Id", "FirmaId", "Ad", "Tur", "AcilisBakiyesi", "AktifMi"],
        ["KasaTransferler"] = ["Id", "FirmaId", "KaynakHesapId", "HedefHesapId", "Tutar", "Tarih", "Aciklama"]
    };
    private static readonly (string Table, string Column, string Parent)[] HistoricalLinks =
    [
        ("CalisanMaasArsivleri", "CalisanId", "Calisanlar"),
        ("OdemePlanlari", "OlusturanKullaniciId", "Kullanicilar"),
        ("OdemeHareketleri", "OlusturanKullaniciId", "Kullanicilar")
    ];

    private async Task<DeletionSnapshot> CaptureLockedAsync(int companyId,
        CompanyDocumentStore store, IDbContextTransaction transaction, CancellationToken ct, bool requirePersistent = false)
    {
        var rows = await ReadRowsAsync(companyId, transaction, ct);
        var retired = await db.Set<CompanyStoredFile>().AsNoTracking()
            .Where(x => x.OwnerCompanyId == companyId && x.RetiredAtUtc != null).Select(x => x.Id).ToListAsync(ct);
        foreach (var id in retired)
            if (await store.HasUntrackedPathReferencesAsync(id, ct) ||
                await db.Set<CompanyStoredFileReference>().AnyAsync(x => x.FileId == id, ct))
                throw new InvalidDataException("Retired company file is still referenced.");
        var links = DocumentLinks(rows);
        var documents = new HashSet<Guid>();
        foreach (var link in links)
        {
            var file = await db.Set<CompanyStoredFile>().AsNoTracking().SingleOrDefaultAsync(x =>
                x.Id == link.FileId && x.OwnerCompanyId == companyId && x.RetiredAtUtc == null, ct);
            var references = await db.Set<CompanyStoredFileReference>().AsNoTracking()
                .Where(x => x.FileId == link.FileId).ToListAsync(ct);
            if (file is null || references.Count != 1 || references.Any(x => x.CompanyId != companyId) ||
                !references.Any(x => x.AttachmentId == link.AttachmentId && x.CheckId == link.CheckId) ||
                references.Count != links.Count(x => x.FileId == link.FileId))
                throw new InvalidDataException("Unverified or shared document ownership.");
            await store.VerifyExclusivePathAsync(link.StoredPath, ct);
            if (documents.Contains(file.Id))
                continue;
            var path = store.ReadObjectPath(file.Id, requirePersistent);
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException("Document storage contains an unsafe entry.");
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            var digest = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
            if (input.Length != file.Length || digest != file.Sha256)
                throw new InvalidDataException("Document content differs from its registered digest.");
            documents.Add(file.Id); // No recovery copy or key is needed for deletion.
        }
        if (await db.Set<CompanyStoredFileReference>().CountAsync(x => x.CompanyId == companyId, ct) != links.Length ||
            await db.Set<CompanyStoredFile>().CountAsync(x => x.OwnerCompanyId == companyId && x.RetiredAtUtc == null, ct) != documents.Count)
            throw new InvalidDataException("Unresolved document inventory.");
        ct.ThrowIfCancellationRequested();
        return new(rows, documents.Order().ToArray());
    }

    internal async Task DeleteCompanyAsync(int companyId, int administratorCompanyId, int administratorUserId,
        Guid operation, CompanyDocumentStore store, CancellationToken ct)
    {
        ValidateTarget(companyId, administratorCompanyId);
        if (operation == Guid.Empty)
            throw new InvalidDataException("Verified administrator and confirmed operation required.");
        // The server deadline releases locks even if the application is stalled or disconnected.
        // Per-command/lock timeouts alone do not bound a transaction spanning many commands/files.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        ct = deadline.Token;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await using (var timeout = Command(tx, TransactionLimitsSql))
            await timeout.ExecuteNonQueryAsync(ct);
        await EnsureDeletionRoleIsIsolatedAsync(tx, ct);
        await LockSnapshotAsync(tx, ct);
        await store.LockCompanyObjectsAsync(companyId, ct);
        await EnsureDeletionSchemaAsync(tx, ct);
        if (!await IndependentPrivatePanelScope.IsCompatibleAsync(db, ct))
            throw new InvalidDataException("The independent private-panel boundary could not be verified.");
        if (!await db.Kullanicilar.AnyAsync(x => x.Id == administratorUserId && x.FirmaId == administratorCompanyId &&
                x.Rol == "SuperAdmin" && x.Firma != null && x.Firma.AktifMi, ct))
            throw new InvalidDataException("Unresolved administrator identity.");
        await EnsureDatabaseGuardAsync(administratorUserId, tx, ct);
        var snapshot = await CaptureLockedAsync(companyId, store, tx, ct, requirePersistent: true);
        store.ConsumeDeletionConfirmation(operation);
        var model = db.Model.GetEntityTypes().ToArray();
        var employees = snapshot.Tables["Calisanlar"].Select(row => row.GetProperty("Id").GetInt32()).ToArray();
        var accounts = snapshot.Tables["KasaHesaplar"].Select(row => row.GetProperty("Id").GetInt32()).ToArray();
        await using (var historical = Command(tx, """
            SELECT EXISTS (SELECT 1 FROM public."CalisanArsivleri"
                WHERE "EskiCalisanId" = ANY(@employees) AND "FirmaId" IS DISTINCT FROM @company)
            OR EXISTS (SELECT 1 FROM public."KasaHareketleri"
                WHERE "KasaHesapId" = ANY(@accounts) AND "FirmaId" IS DISTINCT FROM @company)
            OR EXISTS (SELECT 1 FROM public."KasaTransferler"
                WHERE ("KaynakHesapId" = ANY(@accounts) OR "HedefHesapId" = ANY(@accounts))
                    AND "FirmaId" IS DISTINCT FROM @company)
            """))
        {
            historical.Parameters.AddWithValue("employees", employees);
            historical.Parameters.AddWithValue("accounts", accounts);
            historical.Parameters.AddWithValue("company", companyId);
            if ((bool)(await historical.ExecuteScalarAsync(ct))!)
                throw new InvalidDataException("Company data is referenced outside its archive.");
        }
        foreach (var (table, entity) in Entities())
        {
            var ids = snapshot.Tables[table].Select(row => row.GetProperty("Id").GetInt32()).ToArray();
            foreach (var (source, property) in CompanyDeletionInventory.IncomingLinks(entity, model))
            {
                var sourceTable = source.GetTableName()!;
                var sourceIds = snapshot.Tables.TryGetValue(sourceTable, out var rows)
                    ? rows.Select(row => row.GetProperty("Id").GetInt32()).ToArray() : [];
                var scope = source.ClrType == typeof(CompanyStoredFileReference)
                    ? """s."CompanyId" = @company""" : """s."Id" = ANY(@sourceIds)""";
                await using var check = Command(tx, $"""
                    SELECT EXISTS (SELECT 1 FROM public."{sourceTable}" s
                        WHERE s."{property}" = ANY(@ids) AND NOT ({scope}))
                    """);
                check.Parameters.AddWithValue("company", companyId);
                check.Parameters.AddWithValue("ids", ids);
                check.Parameters.AddWithValue("sourceIds", sourceIds);
                if ((bool)(await check.ExecuteScalarAsync(ct))!)
                    throw new InvalidDataException("Another record refers to the company snapshot.");
            }
        }
        foreach (var table in Tables.Reverse())
        {
            var ids = snapshot.Tables[table].Select(row => row.GetProperty("Id").GetInt32()).ToArray();
            await using var command = Command(tx, $"""DELETE FROM public."{table}" WHERE "Id" = ANY(@ids)""");
            command.Parameters.AddWithValue("ids", ids);
            if (await command.ExecuteNonQueryAsync(ct) != ids.Length)
                throw new InvalidDataException("Company rows changed before deletion.");
        }
        foreach (var fileId in snapshot.FileIds)
        {
            var file = await db.Set<CompanyStoredFile>().SingleAsync(x => x.Id == fileId, ct);
            file.RetiredAtUtc = DateTime.UtcNow;
            db.Add(new CompanyFileCleanupJob { FileId = file.Id, CompanyId = companyId,
                OperationId = operation, EnqueuedAtUtc = file.RetiredAtUtc.Value });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    internal async Task EnsureDeletionRoleIsIsolatedAsync(IDbContextTransaction tx, CancellationToken ct)
    {
        await using var command = Command(tx, """
            WITH owners(oid) AS (
                SELECT datdba FROM pg_catalog.pg_database WHERE datname = current_database()
                UNION SELECT nspowner FROM pg_catalog.pg_namespace WHERE nspname = 'public'
                UNION SELECT relowner FROM pg_catalog.pg_class WHERE oid IN (
                    to_regclass('public."Firmalar"'), to_regclass('public.firmova_company_deletion_owner'),
                    to_regclass('public.firmova_document_file_gates')
                )
                UNION SELECT proowner FROM pg_catalog.pg_proc WHERE oid IN (
                    to_regprocedure('public.firmova_guard_primary_company()'),
                    to_regprocedure('public.firmova_guard_document_file()'),
                    to_regprocedure('public.firmova_document_file_id(text)')
                )
            )
            SELECT EXISTS (
                SELECT FROM pg_catalog.pg_roles r WHERE r.rolname = current_user
                  AND NOT (r.rolsuper OR r.rolcreaterole OR r.rolbypassrls OR r.rolreplication)
                  AND current_setting('session_replication_role') = 'origin'
                  AND NOT pg_catalog.has_parameter_privilege(r.oid, 'session_replication_role', 'SET')
                  AND NOT EXISTS (SELECT FROM owners o WHERE pg_catalog.pg_has_role(r.oid, o.oid, 'MEMBER'))
                  AND NOT pg_catalog.has_column_privilege(r.oid, 'public."Kullanicilar"', 'FirmaId', 'UPDATE')
                  AND NOT pg_catalog.has_table_privilege(r.oid, 'public."Firmalar"', 'TRIGGER,TRUNCATE')
                  AND NOT coalesce(pg_catalog.has_table_privilege(r.oid,
                      to_regclass('public.firmova_company_deletion_owner'),
                      'INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER,MAINTAIN'), false)
                  AND NOT coalesce(pg_catalog.has_any_column_privilege(r.oid,
                      to_regclass('public.firmova_company_deletion_owner'), 'INSERT,UPDATE'), false)
            )
            """);
        if (!(bool)(await command.ExecuteScalarAsync(ct))!)
            throw new InvalidDataException("The application database role can bypass company guards.");
    }

    private async Task EnsureDatabaseGuardAsync(int administratorUserId, IDbContextTransaction tx, CancellationToken ct)
    {
        await using var command = Command(tx, """
            SELECT (SELECT count(*) FROM public.firmova_company_deletion_owner WHERE singleton AND user_id = @admin) = 1
                AND (SELECT count(*) FROM pg_catalog.pg_trigger t
                     WHERE t.tgrelid = 'public."Firmalar"'::regclass AND NOT t.tgisinternal
                       AND t.tgenabled IN ('O', 'A')
                       AND t.tgqual IS NULL AND t.tgnargs = 0
                       AND t.tgfoid = to_regprocedure('public.firmova_guard_primary_company()')
                       AND ((t.tgname = 'firmova_guard_primary_company_row' AND t.tgtype = 27
                             AND t.tgattr::text = (SELECT a.attnum::text FROM pg_catalog.pg_attribute a
                                WHERE a.attrelid = t.tgrelid AND a.attname = 'Id' AND NOT a.attisdropped))
                         OR (t.tgname = 'firmova_guard_primary_company_truncate' AND t.tgtype = 34
                             AND t.tgattr::text = ''))) = 2
            """);
        command.Parameters.AddWithValue("admin", administratorUserId);
        if (!(bool)(await command.ExecuteScalarAsync(ct))!)
            throw new InvalidDataException("Database company guards are not verified.");
    }

    internal async Task EnsureDeletionSchemaAsync(IDbContextTransaction tx, CancellationToken ct)
    {
        await using var command = Command(tx, """
            SELECT c.relname
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p')
              AND (EXISTS (
                  SELECT 1 FROM pg_catalog.pg_attribute a
                  WHERE a.attrelid = c.oid AND a.attnum > 0 AND NOT a.attisdropped AND a.attname = 'FirmaId'
              ) OR EXISTS (
                  SELECT 1 FROM pg_catalog.pg_constraint fk
                  JOIN pg_catalog.pg_class target ON target.oid = fk.confrelid
                  JOIN pg_catalog.pg_namespace target_schema ON target_schema.oid = target.relnamespace
                  WHERE fk.conrelid = c.oid AND fk.contype = 'f'
                    AND target_schema.nspname = 'public' AND target.relname = ANY(@companyTables)
              ))
              AND NOT (c.relname = ANY(@knownTables))
            LIMIT 1
            """);
        command.Parameters.AddWithValue("companyTables", Tables);
        command.Parameters.AddWithValue("knownTables",
            Tables.Concat(["CompanyStoredFileReferences", "firmova_company_deletion_owner"]).ToArray());
        if (await command.ExecuteScalarAsync(ct) is not null)
            throw new InvalidDataException("The database contains a company relationship outside the recovery model.");
    }

    private async Task<Dictionary<string, JsonElement[]>> ReadRowsAsync(int companyId,
        IDbContextTransaction transaction, CancellationToken ct)
    {
        var result = new Dictionary<string, JsonElement[]>();
        foreach (var table in Tables)
        {
            var scope = table switch
            {
                "Firmalar" => """r."Id" = @company""",
                "FaturaKalemleri" => """r."FaturaId" IN (SELECT "Id" FROM public."Faturalar" WHERE "FirmaId" = @company)""",
                _ => """r."FirmaId" = @company"""
            };
            await using var command = Command(transaction, $"""
                SELECT coalesce(jsonb_agg(to_jsonb(r) ORDER BY r."Id"), '[]'::jsonb)::text
                FROM public."{table}" r WHERE {scope}
                """);
            command.Parameters.AddWithValue("company", companyId);
            result.Add(table, JsonSerializer.Deserialize<JsonElement[]>((string)(await command.ExecuteScalarAsync(ct))!)!);
        }
        ValidateRows(companyId, result);
        if (await CompanyAuditActorPolicy.HasUnresolvedAsync(db, companyId, ct))
            throw new InvalidDataException("Unresolved historical reference in IslemGecmisleri.KullaniciId.");
        await ValidateArchivedEmployeesAsync(companyId, result["CalisanArsivleri"], transaction, ct);
        return result;
    }

    private async Task LockSnapshotAsync(IDbContextTransaction transaction, CancellationToken ct)
    {
        // Fence inserts/changes between the tenant inventory and external/historical-link checks.
        // Row-only locks cannot fence a new FK-less historical reference. Ordinary reads remain
        // available; writes wait only until commit, rollback or the server transaction deadline.
        await using var locks = Command(transaction, "LOCK TABLE " +
            string.Join(", ", Tables.Select(table => $"public.\"{table}\"")) + " IN SHARE ROW EXCLUSIVE MODE");
        await locks.ExecuteNonQueryAsync(ct);
    }

    private async Task ValidateArchivedEmployeesAsync(int companyId, JsonElement[] rows,
        IDbContextTransaction transaction, CancellationToken ct)
    {
        var historicalIds = rows.Select(row => row.GetProperty("EskiCalisanId").GetInt32()).ToArray();
        await using var command = Command(transaction, """
            SELECT EXISTS (SELECT 1 FROM public."Calisanlar"
                WHERE "Id" = ANY(@ids) AND "FirmaId" IS DISTINCT FROM @company)
            """);
        command.Parameters.AddWithValue("ids", historicalIds);
        command.Parameters.AddWithValue("company", companyId);
        if ((bool)(await command.ExecuteScalarAsync(ct))!)
            throw new InvalidDataException("Archived employee identity collides with an employee outside the company.");
    }

    private sealed record DocumentLink(Guid FileId, int? AttachmentId, int? CheckId, string StoredPath);

    private static DocumentLink[] DocumentLinks(Dictionary<string, JsonElement[]> tables)
    {
        var logo = tables["Firmalar"].Single().GetProperty("LogoYolu").GetString();
        if (!string.IsNullOrEmpty(logo) && !logo.StartsWith("data:", StringComparison.Ordinal))
            throw new InvalidDataException("Unverified legacy logo.");
        var links = new List<DocumentLink>();
        foreach (var (table, column) in new[] { ("EkDosyalar", "DosyaYolu"), ("Cekler", "ResimYolu") })
        foreach (var row in tables[table])
        {
            var path = row.GetProperty(column).GetString();
            if (table == "Cekler" && string.IsNullOrEmpty(path))
                continue;
            var id = row.GetProperty("Id").GetInt32();
            const string prefix = "/CompanyDocument/";
            if (CompanyDocumentStore.TryGetStorageId(path, out var currentId))
            {
                links.Add(new(currentId, table == "EkDosyalar" ? id : null, table == "Cekler" ? id : null, path!));
                continue;
            }
            if (path is null || !path.StartsWith(prefix, StringComparison.Ordinal) ||
                path.Length <= prefix.Length + 33 || path[prefix.Length + 32] != '.' ||
                !Guid.TryParseExact(path.AsSpan(prefix.Length, 32), "N", out var fileId) ||
                path[(prefix.Length + 33)..].Any(c => !char.IsAsciiLetterOrDigit(c)))
                throw new InvalidDataException("Unverified legacy document path.");
            links.Add(new(fileId, table == "EkDosyalar" ? id : null, table == "Cekler" ? id : null, path));
        }
        return links.ToArray();
    }


    private void ValidateTarget(int companyId, int administratorCompanyId)
    {
        ValidateModel(companyId);
        if (administratorCompanyId <= 0 || companyId == administratorCompanyId)
            throw new InvalidOperationException("Protected company or unknown administrator company.");
    }

    private void ValidateModel(int companyId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(companyId, 2);
        if (!db.Database.IsNpgsql() || !Entities().Keys.Concat(ExistingColumns.Keys).ToHashSet().SetEquals(Tables))
            throw new NotSupportedException("Unsupported company recovery model.");
    }

    private Dictionary<string, IEntityType> Entities() => db.Model.GetEntityTypes()
        .Where(entity => entity.FindProperty("FirmaId") is not null ||
            entity.GetTableName() is "Firmalar" or "FaturaKalemleri")
        .ToDictionary(entity => entity.GetTableName()!);

    private void ValidateRows(int companyId, Dictionary<string, JsonElement[]> tables)
    {
        if (!tables.Keys.ToHashSet().SetEquals(Tables) || tables["Firmalar"].Length != 1)
            throw new InvalidDataException("Incomplete company snapshot.");
        var ids = tables.ToDictionary(pair => pair.Key, pair =>
            pair.Value.Select(row => row.GetProperty("Id").GetInt32()).ToHashSet());
        foreach (var (table, column, parent) in HistoricalLinks)
        {
            if (tables[table].Select(row => row.GetProperty(column)).Any(value =>
                value.ValueKind != JsonValueKind.Null && !ids[parent].Contains(value.GetInt32())))
                throw new InvalidDataException($"Unresolved historical reference in {table}.{column}.");
        }
        var entities = Entities();
        foreach (var (table, rows) in tables)
        {
            var entity = entities.GetValueOrDefault(table);
            var columns = entity is null ? ExistingColumns[table].ToHashSet()
                : entity.GetProperties().Select(property => property.GetColumnName()!).ToHashSet();
            if (table == "KasaHareketleri")
                columns.Add("KasaHesapId");
            foreach (var row in rows)
            {
                if (!row.EnumerateObject().Select(property => property.Name).ToHashSet().SetEquals(columns) ||
                    (table == "Firmalar" && row.GetProperty("Id").GetInt32() != companyId) ||
                    (table is not ("Firmalar" or "FaturaKalemleri") &&
                        row.GetProperty("FirmaId").GetInt32() != companyId))
                    throw new InvalidDataException($"Invalid company scope or columns in {table}.");
                if (table is "KasaHareketleri" or "KasaTransferler")
                {
                    var links = table == "KasaHareketleri" ? new[] { "KasaHesapId" } : ["KaynakHesapId", "HedefHesapId"];
                    if (links.Select(row.GetProperty).Any(value => value.ValueKind != JsonValueKind.Null &&
                        !ids["KasaHesaplar"].Contains(value.GetInt32())))
                        throw new InvalidDataException("Cash account is outside the company snapshot.");
                }
                // EskiCalisanId is a historical identifier and can outlive the employee row.
                if (table == "CalisanArsivleri" && row.GetProperty("EskiCalisanId").GetInt32() <= 0)
                    throw new InvalidDataException("Unresolved archived employee identity.");
                if (entity is null)
                    continue;
                foreach (var foreignKey in entity.GetForeignKeys())
                {
                    var parent = foreignKey.PrincipalEntityType.GetTableName()!;
                    var idIndex = foreignKey.PrincipalKey.Properties.Select((p, i) => (p, i)).Where(x => x.p.Name == "Id").Select(x => x.i).DefaultIfEmpty(-1).Single();
                    if (idIndex < 0 || foreignKey.PrincipalKey.Properties.Any(p => p.Name is not ("Id" or "FirmaId")))
                        throw new InvalidDataException("Unsupported composite company reference.");
                    var value = row.GetProperty(foreignKey.Properties[idIndex].GetColumnName()!);
                    if (value.ValueKind != JsonValueKind.Null && !ids[parent].Contains(value.GetInt32()))
                        throw new InvalidDataException($"External foreign key in {table}.");
                }
            }
        }
    }

    private NpgsqlCommand Command(IDbContextTransaction transaction, string sql) =>
        new(sql, (NpgsqlConnection)db.Database.GetDbConnection(), (NpgsqlTransaction)transaction.GetDbTransaction());
}
