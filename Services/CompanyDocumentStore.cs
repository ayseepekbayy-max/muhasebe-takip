using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

public sealed class CompanyDocumentStore(AppDbContext db, IWebHostEnvironment environment)
{
    private bool fileGatesVerified;
    private string Root => CompanyDiskStorage.Root(environment);
    internal string Objects => Path.Combine(Root, "objects");
    internal string Quarantine => Path.Combine(Root, "quarantine");
    public string ObjectPath(Guid id)
    {
        var candidates = new List<string>();
        var flat = Path.Combine(Objects, id.ToString("N"));
        if (IsRegularObject(flat)) candidates.Add(flat);
        foreach (var kind in new[] { "cekler", "faturalar", "cariler" })
        {
            var directory = Path.Combine(Objects, kind);
            if (!IsSafeDirectory(directory)) continue;
            foreach (var pattern in new[] { $"*{id:N}.*", $"*{id:D}.*" })
            foreach (var path in Directory.EnumerateFileSystemEntries(directory, pattern))
                if (TryGetStorageId($"/uploads/{CompanyDiskStorage.PrivateDirectory}/objects/{kind}/{Path.GetFileName(path)}", out var found) &&
                    found == id && IsRegularObject(path)) candidates.Add(path);
        }
        candidates = candidates.Distinct(StringComparer.Ordinal).ToList();
        if (candidates.Count > 1) throw new InvalidDataException("Ambiguous stored document location.");
        return candidates.SingleOrDefault() ?? flat;
    }

    internal static bool TryGetStorageId(string? storedPath, out Guid id)
    {
        id = Guid.Empty;
        if (storedPath is null) return false;
        var match = System.Text.RegularExpressions.Regex.Match(storedPath,
            @"\A/uploads/\.firmova-private/objects/(?:cekler|faturalar|cariler)/(?:fatura-[0-9]+-|cari-[0-9]+-)?([0-9a-f]{32}|[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12})\.(?:pdf|png|jpg|jpeg|webp|xlsx|xls|docx)\z",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.NonBacktracking);
        return match.Success && Guid.TryParse(match.Groups[1].Value, out id) && id != Guid.Empty;
    }

    // Called only by the trusted, just-published upload flow, never as a legacy backfill.
    internal async Task RegisterPublishedAsync(string storedPath, EkDosya? attachment, Cek? check, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || !storedPath.StartsWith($"/uploads/{CompanyDiskStorage.PrivateDirectory}/objects/", StringComparison.Ordinal)) return;
        if (db.Database.CurrentTransaction is null || (attachment is null) == (check is null) || !TryGetStorageId(storedPath, out var id))
            throw new InvalidDataException("Trusted document transaction required.");
        var company = attachment?.FirmaId ?? check!.FirmaId;
        // Primary company is outside permanent-deletion tracking; its existing document lifecycle stays unchanged.
        if (company == 1) return;
        await LockObjectAsync(id, ct);
        var kind = check is not null ? "cekler" : attachment!.FaturaId.HasValue ? "faturalar" : "cariler";
        var prefix = check is not null ? "" : attachment!.FaturaId.HasValue ? $"fatura-{attachment.FaturaId}-" : $"cari-{attachment.CariKartId}-";
        var path = AccountingDocuments.ResolveDocument(environment.WebRootPath, storedPath, kind, prefix)
            ?? throw new InvalidDataException("Unsafe published document.");
        if (attachment is not null && !await db.EkDosyalar.AsNoTracking().AnyAsync(x => x.Id == attachment.Id && x.FirmaId == company && x.DosyaYolu == storedPath &&
            ((x.FaturaId != null && x.CariKartId == null && x.Fatura != null && x.Fatura.FirmaId == company) ||
             (x.CariKartId != null && x.FaturaId == null && x.CariKart != null && x.CariKart.FirmaId == company)), ct))
            throw new InvalidDataException("Document parent mismatch.");
        if (check is not null && !await db.Cekler.AsNoTracking().AnyAsync(x => x.Id == check.Id && x.FirmaId == company && x.ResimYolu == storedPath, ct))
            throw new InvalidDataException("Cheque mismatch.");
        await VerifyExclusivePathAsync(storedPath, ct);
        await using var input = File.OpenRead(path);
        var length = input.Length;
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
        db.Add(new CompanyStoredFile { Id = id, OwnerCompanyId = company, Length = length, Sha256 = digest });
        db.Add(new CompanyStoredFileReference { FileId = id, CompanyId = company, AttachmentId = attachment?.Id, CheckId = check?.Id });
        await db.SaveChangesAsync(ct);
    }

    internal async Task VerifyExclusivePathAsync(string storedPath, CancellationToken ct)
    {
        if (TryGetStorageId(storedPath, out var storageId) &&
            Path.GetFullPath(Path.Combine(environment.WebRootPath, storedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))) != ReadObjectPath(storageId, requirePersistent: true))
            throw new InvalidDataException("Stored document path differs from registered object location.");
        var key = LegacyCompanyFileReport.ComparisonPath(storedPath);
        var paths = await db.Cekler.Where(x => x.ResimYolu != null && x.ResimYolu != "").Select(x => x.ResimYolu!).ToListAsync(ct);
        paths.AddRange(await db.EkDosyalar.Select(x => x.DosyaYolu).ToListAsync(ct));
        paths.AddRange(await db.Firmalar.Where(x => x.LogoYolu != "" && !x.LogoYolu.StartsWith("data:")).Select(x => x.LogoYolu).ToListAsync(ct));
        if (paths.Count(x => LegacyCompanyFileReport.ComparisonPath(x) == key) != 1)
            throw new InvalidDataException("Shared or ambiguous physical document reference.");
    }

    internal async Task<bool> HasUntrackedPathReferencesAsync(Guid id, CancellationToken ct)
    {
        var paths = await db.EkDosyalar.Select(x => x.DosyaYolu).ToListAsync(ct);
        paths.AddRange(await db.Cekler.Where(x => x.ResimYolu != null).Select(x => x.ResimYolu!).ToListAsync(ct));
        paths.AddRange(await db.Firmalar.Where(x => x.LogoYolu != "" && !x.LogoYolu.StartsWith("data:")).Select(x => x.LogoYolu).ToListAsync(ct));
        return paths.Any(path => {
            var normalized = LegacyCompanyFileReport.ComparisonPath(path);
            return TryGetStorageId(normalized, out var found) && found == id ||
                normalized.StartsWith($"/companydocument/{id:N}.", StringComparison.Ordinal);
        });
    }

    internal async Task<Guid?> LockCheckReferenceAsync(Cek check, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return null;
        var reference = await db.Set<CompanyStoredFileReference>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CheckId == check.Id && x.CompanyId == check.FirmaId, ct);
        if (reference is null) return null;
        await LockObjectAsync(reference.FileId, ct);
        var file = await LockFileAsync(db, reference.FileId, ct);
        if (file.OwnerCompanyId != check.FirmaId || file.RetiredAtUtc is not null)
            throw new InvalidDataException("Invalid cheque file ownership.");
        return file.Id;
    }

    internal async Task RetireRemovedReferenceAsync(Guid id, int company, CancellationToken ct)
    {
        if (await db.Set<CompanyStoredFileReference>().AnyAsync(x => x.FileId == id, ct)) return;
        var file = await db.Set<CompanyStoredFile>().SingleAsync(x => x.Id == id && x.OwnerCompanyId == company, ct);
        file.RetiredAtUtc = DateTime.UtcNow;
        if (company > 1)
            db.Add(new CompanyFileCleanupJob { FileId = id, CompanyId = company, OperationId = Guid.NewGuid(), EnqueuedAtUtc = file.RetiredAtUtc.Value });
        await db.SaveChangesAsync(ct);
    }

    internal void ConsumeDeletionConfirmation(Guid operation)
    {
        CompanyDiskStorage.EnsurePrivateDirectory(Root);
        var directory = Path.Combine(Root, "deletion-confirmations");
        CompanyDiskStorage.EnsurePrivateDirectory(directory);
        using var marker = new FileStream(Path.Combine(directory, operation.ToString("N")), FileMode.CreateNew);
        marker.Flush(flushToDisk: true);
    }
    internal string PreviousObjectPath(Guid id) =>
        Path.Combine(environment.ContentRootPath, "App_Data", "CompanyDocuments", "objects", id.ToString("N"));
    internal bool HasPreviousObject(Guid id) => IsRegularObject(PreviousObjectPath(id));

    public string ReadObjectPath(Guid id, bool requirePersistent = false)
    {
        var path = ObjectPath(id);
        var current = IsRegularObject(path);
        var previous = HasPreviousObject(id);
        if (previous && (current || requirePersistent))
            throw new InvalidDataException("Document location is ambiguous or outside persistent storage.");
        return previous ? PreviousObjectPath(id) : path;
    }

    private static bool IsRegularObject(string path)
    {
        // Walk from the root so even a dangling link or an inaccessible parent fails closed.
        var parent = Path.GetDirectoryName(path);
        if (parent is not null && !IsSafeDirectory(parent))
            return false;
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
                throw new InvalidDataException("Document storage contains an unsafe entry.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
    }

    internal static bool IsSafeDirectory(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is not null && !IsSafeDirectory(parent))
            return false;
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
                throw new InvalidDataException("Document storage contains an unsafe directory.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    internal static async Task<CompanyStoredFile> LockFileAsync(AppDbContext context, Guid id, CancellationToken ct)
    {
        var rows = await context.Set<CompanyStoredFile>()
            .FromSqlInterpolated($"""SELECT * FROM public."CompanyStoredFiles" WHERE "Id" = {id} FOR UPDATE""")
            .ToListAsync(ct);
        return rows.Single();
    }

    internal async Task LockObjectAsync(Guid id, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null || id == Guid.Empty)
            throw new InvalidDataException("A document transaction and storage identity are required.");
        await VerifyFileGatesAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_catalog.pg_advisory_xact_lock(pg_catalog.hashtextextended({id.ToString("D")}::text, 471006))", ct);
    }

    internal async Task<bool> TryLockObjectAsync(Guid id, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null || id == Guid.Empty)
            throw new InvalidDataException("A document transaction and storage identity are required.");
        await VerifyFileGatesAsync(ct);
        return await db.Database.SqlQuery<bool>(
            $"SELECT pg_catalog.pg_try_advisory_xact_lock(pg_catalog.hashtextextended({id.ToString("D")}::text, 471006)) AS \"Value\"").SingleAsync(ct);
    }

    private async Task VerifyFileGatesAsync(CancellationToken ct)
    {
        if (!fileGatesVerified)
        {
            var ready = await db.Database.SqlQueryRaw<bool>("""
                SELECT count(*) = 6 AS "Value" FROM pg_catalog.pg_trigger t
                JOIN pg_catalog.pg_proc p ON p.oid=t.tgfoid
                WHERE t.tgname='firmova_document_file_gate' AND NOT t.tgisinternal
                  AND t.tgenabled IN ('O','A') AND t.tgtype=31 AND t.tgnargs=0 AND t.tgqual IS NULL
                  AND p.oid=to_regprocedure('public.firmova_guard_document_file()')
                  AND p.prosecdef AND p.proconfig=ARRAY['search_path=pg_catalog, pg_temp']
                  AND t.tgrelid IN ('public."Firmalar"'::regclass,'public."Cekler"'::regclass,
                    'public."EkDosyalar"'::regclass,'public."CompanyStoredFiles"'::regclass,
                    'public."CompanyStoredFileReferences"'::regclass,'public."CompanyFileCleanupJobs"'::regclass)
                """).SingleAsync(ct);
            if (!ready) throw new InvalidDataException("Mandatory database document file gates are missing or inactive.");
            fileGatesVerified = true;
        }
    }

    internal async Task LockCompanyObjectsAsync(int company, CancellationToken ct)
    {
        await VerifyFileGatesAsync(ct);
        var ids = await db.Set<CompanyStoredFile>().Where(x => x.OwnerCompanyId == company)
            .Select(x => x.Id).OrderBy(x => x).ToListAsync(ct);
        foreach (var id in ids) await LockObjectAsync(id, ct);
    }
}
