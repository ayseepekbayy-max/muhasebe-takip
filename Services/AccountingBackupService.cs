using System.Data;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Services;

// Explicit allowlist: never serialize users, credentials, private chat, or arbitrary EF types.
public sealed class AccountingBackupService(AppDbContext db, IWebHostEnvironment environment)
{
    public const long MaxPackageBytes = 100L * 1024 * 1024;
    public const long MaxExpandedBytes = 300L * 1024 * 1024;
    public const long MaxFileBytes = 25L * 1024 * 1024;
    public const long MaxManifestBytes = 32L * 1024 * 1024;
    public const int MaxRows = 250_000;
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false, MaxDepth = 64 };
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".xlsx", ".xls", ".docx" };

    public sealed class Package
    {
        public string Format { get; set; } = "FirmovaAccountingBackup";
        public int Version { get; set; } = 2;
        public Guid BackupId { get; set; } = Guid.NewGuid();
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public string CompanyName { get; set; } = "";
        public Dictionary<string, List<Dictionary<string, JsonElement>>> Tables { get; set; } = new();
        public List<UserReference> Users { get; set; } = new();
        public List<Document> Documents { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
    public sealed record UserReference(int Id, string Username, string Email);
    public sealed record Document(string Table, int RowId, string Property, string Entry, long Length, string Sha256);
    public sealed record RestoreResult(int Records, int Files, IReadOnlyList<string> Warnings);
    public sealed class BackupException(string message) : Exception(message);
    private sealed record Table(Type Type, Func<int, CancellationToken, Task<List<object>>> Load);

    private Table TableFor<T>() where T : class => new(typeof(T), async (firmaId, ct) =>
    {
        IQueryable<T> query = db.Set<T>().AsNoTracking();
        query = typeof(T) == typeof(FaturaKalem)
            ? query.Where(x => db.Faturalar.Any(f => f.FirmaId == firmaId && f.Id == EF.Property<int>(x, "FaturaId")))
            : query.Where(x => EF.Property<int?>(x, "FirmaId") == firmaId);
        return (await query.ToListAsync(ct)).Cast<object>().ToList();
    });

    // Parents precede children, including relationships not declared as EF foreign keys.
    private Table[] Tables() =>
    [
        TableFor<CariKart>(), TableFor<StokUrun>(), TableFor<Calisan>(), TableFor<Musteri>(),
        TableFor<Cek>(), TableFor<FaturaNumaraAyari>(), TableFor<OdemePlani>(), TableFor<MaliyetKaydi>(),
        TableFor<Fatura>(), TableFor<MusteriIs>(), TableFor<CalisanAvans>(),
        TableFor<CalisanPuantaj>(), TableFor<CalisanMaasArsiv>(), TableFor<StokHareket>(),
        TableFor<FaturaKalem>(), TableFor<MusteriMasraf>(), TableFor<KasaHareket>(),
        TableFor<EkDosya>(), TableFor<OdemeHareketi>(), TableFor<OdemeBildirimGecmisi>(),
        TableFor<OdemeBildirimGizleme>(), TableFor<YoneticiNotu>(), TableFor<IslemGecmisi>()
    ];
    private static readonly Dictionary<string, string> Links = new()
    {
        ["CariKartId"] = nameof(CariKart), ["StokUrunId"] = nameof(StokUrun),
        ["CalisanId"] = nameof(Calisan), ["MusteriId"] = nameof(Musteri),
        ["MusteriIsId"] = nameof(MusteriIs), ["FaturaId"] = nameof(Fatura),
        ["OdemePlaniId"] = nameof(OdemePlani)
    };
    private static readonly string[] UserProperties = ["KullaniciId", "OlusturanKullaniciId"];
    private IEntityType Entity(Table t) => db.Model.FindEntityType(t.Type)!;
    private IEnumerable<IProperty> Properties(Table t) => Entity(t).GetProperties().Where(p => p.PropertyInfo != null);
    private static int Id(Dictionary<string, JsonElement> row) => row["Id"].GetInt32();
    private static bool HasValue(JsonElement value) => value.ValueKind != JsonValueKind.Null;

    public async Task<byte[]> ExportAsync(int firmaId, CancellationToken ct = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var package = new Package
        {
            CompanyName = await db.Firmalar.Where(x => x.Id == firmaId).Select(x => x.FirmaAdi).SingleAsync(ct),
            Users = await db.Kullanicilar.Where(x => x.FirmaId == firmaId)
                .Select(x => new UserReference(x.Id, x.KullaniciAdi, x.Email)).ToListAsync(ct)
        };
        foreach (var table in Tables())
        {
            package.Tables[table.Type.Name] = (await table.Load(firmaId, ct)).Select(entity =>
                Properties(table).ToDictionary(p => p.Name,
                    p => JsonSerializer.SerializeToElement(p.PropertyInfo!.GetValue(entity), p.ClrType, JsonOptions))).ToList();
        }
        // Deleted users may still be named in historical optional audit references.
        var referencedUsers = package.Tables.Values.SelectMany(x => x)
            .SelectMany(row => UserProperties.Where(row.ContainsKey).Select(key => row[key]))
            .Where(HasValue).Select(x => x.GetInt32()).Distinct();
        foreach (var id in referencedUsers)
            if (package.Users.All(x => x.Id != id)) package.Users.Add(new UserReference(id, "", ""));
        Validate(package);
        using var output = new MemoryStream();
        long expanded = 0;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var (table, property) in new[] { (nameof(EkDosya), "DosyaYolu"), (nameof(Cek), "ResimYolu") })
            foreach (var row in package.Tables[table])
            {
                var url = row[property].ValueKind == JsonValueKind.Null ? null : row[property].GetString();
                if (string.IsNullOrWhiteSpace(url))
                {
                    if (table == nameof(EkDosya)) throw new BackupException("Bir belge kaydının dosya yolu boş. Eksiksiz yedek oluşturulamadı.");
                    continue;
                }
                var path = SourceDocumentPath(url);
                if (!File.Exists(path)) throw new BackupException("Kayıtlara bağlı bir belge sunucuda bulunamadı. Eksiksiz yedek oluşturulamadı; önce eksik belgeyi kontrol edin.");
                await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (source.Length > MaxFileBytes) throw new BackupException("Bir belge 25 MB sınırını aşıyor.");
                expanded += source.Length;
                if (expanded > MaxExpandedBytes) throw new BackupException("Yedekteki belgeler toplam boyut sınırını aşıyor.");
                using var bytes = new MemoryStream();
                await source.CopyToAsync(bytes, ct);
                var data = bytes.ToArray();
                var entryName = $"files/{Guid.NewGuid():N}{Path.GetExtension(path).ToLowerInvariant()}";
                var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                await using (var target = entry.Open()) await target.WriteAsync(data, ct);
                package.Documents.Add(new Document(table, Id(row), property, entryName, data.LongLength,
                    Convert.ToHexString(SHA256.HashData(data))));
            }
            var manifest = JsonSerializer.SerializeToUtf8Bytes(package, JsonOptions);
            if (manifest.LongLength > MaxManifestBytes) throw new BackupException("Yedek kayıtları 32 MB sınırını aşıyor.");
            await using var manifestStream = zip.CreateEntry("manifest.json", CompressionLevel.Fastest).Open();
            await manifestStream.WriteAsync(manifest, ct);
        }
        if (output.Length > MaxPackageBytes) throw new BackupException("Yedek dosyası 100 MB sınırını aşıyor.");
        await transaction.CommitAsync(ct);
        return output.ToArray();
    }

    private string SourceDocumentPath(string url)
    {
        if (!url.StartsWith("/uploads/", StringComparison.Ordinal) || url.Contains('\\') || url.Contains('%') ||
            url.Split('/').Any(x => x is "." or "..") || !Extensions.Contains(Path.GetExtension(url)))
            throw new BackupException("Bir belge yolu desteklenmiyor; yedek oluşturulamadı.");
        var root = Path.GetFullPath(environment.WebRootPath);
        var result = Path.GetFullPath(Path.Combine(root, url.TrimStart('/')));
        if (!result.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new BackupException("Geçersiz belge yolu.");
        // Do not follow symbolic links out of the approved document directory.
        for (var current = result; current != root; current = Path.GetDirectoryName(current)!)
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new BackupException("Bağlantılı dosya yolları yedeklenemez.");
        return result;
    }

    private void Validate(Package package)
    {
        var tables = Tables();
        if (package.Format != "FirmovaAccountingBackup" || package.Version != 2 || package.BackupId == Guid.Empty ||
            package.Tables == null || package.Users == null || package.Documents == null || package.Warnings == null ||
            package.Tables.Count != tables.Length || tables.Any(t => !package.Tables.ContainsKey(t.Type.Name)) ||
            package.Tables.Values.Any(x => x == null) || package.Tables.Values.Sum(x => (long)x.Count) > MaxRows)
            throw new BackupException("Yedek biçimi, sürümü veya kapsamı geçersiz.");
        var ids = package.Tables.ToDictionary(x => x.Key, x => x.Value.Select(Id).ToHashSet());
        if (package.Users.Any(x => x == null || x.Id <= 0) || package.Users.Select(x => x.Id).Distinct().Count() != package.Users.Count)
            throw new BackupException("Yedekteki kullanıcı başvuruları geçersiz.");
        var userIds = package.Users.Select(x => x.Id).ToHashSet();
        foreach (var table in tables)
        {
            var rows = package.Tables[table.Type.Name];
            if (ids[table.Type.Name].Count != rows.Count || ids[table.Type.Name].Any(x => x <= 0))
                throw new BackupException("Yedekte yinelenen veya geçersiz kayıt numarası var.");
            var properties = Properties(table).ToArray();
            foreach (var row in rows)
            {
                if (row.Count != properties.Length || properties.Any(p => !row.ContainsKey(p.Name)))
                    throw new BackupException("Yedek kayıt alanları bu sürümle uyumlu değil.");
                foreach (var property in properties)
                {
                    var json = row[property.Name];
                    if (!HasValue(json) && !property.IsNullable)
                        throw new BackupException("Yedekte zorunlu bir alan boş.");
                    var value = json.Deserialize(property.ClrType, JsonOptions);
                    if (value is string text && property.GetMaxLength() is int max && text.Length > max)
                        throw new BackupException("Yedekte bir metin alanı izin verilen uzunluğu aşıyor.");
                    if (HasValue(json) && Links.TryGetValue(property.Name, out var parent) && !ids[parent].Contains(json.GetInt32()))
                        throw new BackupException("Yedekte eksik veya firma dışına ait kayıt bağlantısı var.");
                    if (HasValue(json) && UserProperties.Contains(property.Name) && !userIds.Contains(json.GetInt32()))
                        throw new BackupException("Yedekte eksik kullanıcı bağlantısı var.");
                }
            }
        }
        var documents = package.Documents;
        if (documents.Select(x => x.Entry).Distinct(StringComparer.Ordinal).Count() != documents.Count ||
            documents.Select(x => (x.Table, x.RowId, x.Property)).Distinct().Count() != documents.Count)
            throw new BackupException("Yedekte yinelenen belge başvurusu var.");
        foreach (var document in documents)
        {
            if (!((document.Table == nameof(EkDosya) && document.Property == "DosyaYolu") ||
                  (document.Table == nameof(Cek) && document.Property == "ResimYolu")) ||
                !ids[document.Table].Contains(document.RowId) || document.Length < 0 || document.Length > MaxFileBytes ||
                !document.Entry.StartsWith("files/", StringComparison.Ordinal) || document.Entry.Count(c => c == '/') != 1 ||
                document.Entry.Contains('\\') || document.Entry.Contains("..") || !Extensions.Contains(Path.GetExtension(document.Entry)))
                throw new BackupException("Yedekte geçersiz belge başvurusu var.");
        }
        foreach (var (table, property) in new[] { (nameof(EkDosya), "DosyaYolu"), (nameof(Cek), "ResimYolu") })
        foreach (var row in package.Tables[table])
        {
            var hasPath = row[property].ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(row[property].GetString());
            // Export builds document entries after reading rows; restore calls this once entries exist.
            if (package.Documents.Count > 0 && hasPath && !documents.Any(x => x.Table == table && x.RowId == Id(row) && x.Property == property))
                throw new BackupException("Yedekte bir belgenin içeriği eksik.");
        }
    }

    public async Task<RestoreResult> RestoreAsync(Stream input, int firmaId, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(input, buffer, MaxPackageBytes, ct);
        buffer.Position = 0;
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, true);
        if (zip.Entries.Count > MaxRows + 1 || zip.Entries.Sum(x => x.Length) > MaxExpandedBytes + MaxManifestBytes ||
            zip.Entries.Select(x => x.FullName).Distinct(StringComparer.Ordinal).Count() != zip.Entries.Count)
            throw new BackupException("Yedek paketi boyutu veya dosya listesi geçersiz.");
        var manifest = zip.GetEntry("manifest.json") ?? throw new BackupException("Yedek içeriği bulunamadı.");
        if (manifest.Length > MaxManifestBytes) throw new BackupException("Yedek kayıtları çok büyük.");
        await using var manifestStream = manifest.Open();
        using var manifestBytes = new MemoryStream();
        await CopyLimitedAsync(manifestStream, manifestBytes, MaxManifestBytes, ct);
        manifestBytes.Position = 0;
        var package = await JsonSerializer.DeserializeAsync<Package>(manifestBytes, JsonOptions, ct)
            ?? throw new BackupException("Yedek okunamadı.");
        Validate(package);
        var expected = package.Documents.Select(x => x.Entry).Append("manifest.json").ToHashSet(StringComparer.Ordinal);
        if (zip.Entries.Count != expected.Count || zip.Entries.Any(x => !expected.Contains(x.FullName)))
            throw new BackupException("Yedekte eksik veya beklenmeyen dosya var.");
        foreach (var row in package.Tables[nameof(EkDosya)])
            if (!package.Documents.Any(x => x.Table == nameof(EkDosya) && x.RowId == Id(row)))
                throw new BackupException("Yedekte bir belgenin içeriği eksik.");
        foreach (var row in package.Tables[nameof(Cek)])
            if (row["ResimYolu"].ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(row["ResimYolu"].GetString()) &&
                !package.Documents.Any(x => x.Table == nameof(Cek) && x.RowId == Id(row)))
                throw new BackupException("Yedekte bir çek görseli eksik.");

        var restoreId = Guid.NewGuid().ToString("N");
        var stage = Path.Combine(environment.ContentRootPath, "App_Data", "RestoreStaging", restoreId);
        var final = Path.Combine(environment.WebRootPath, "uploads", "yedek", restoreId);
        Directory.CreateDirectory(stage);
        var committed = false;
        try
        {
            foreach (var document in package.Documents)
            {
                var entry = zip.GetEntry(document.Entry)!;
                if (entry.Length != document.Length) throw new BackupException("Belge boyutu doğrulanamadı.");
                var target = Path.Combine(stage, Path.GetFileName(document.Entry));
                await using (var source = entry.Open())
                await using (var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                    await CopyLimitedAsync(source, destination, MaxFileBytes, ct);
                await using var verify = File.OpenRead(target);
                if (verify.Length != document.Length) throw new BackupException("Belgenin gerçek boyutu doğrulanamadı.");
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(verify, ct));
                if (!string.Equals(hash, document.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new BackupException("Belge bütünlüğü doğrulanamadı. Geri yükleme iptal edildi.");
            }
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var tables = Tables();
            foreach (var table in tables)
                if ((await table.Load(firmaId, ct)).Count > 0)
                    throw new BackupException("Bu firmada muhasebe kayıtları var. Tam yedek yalnızca boş muhasebe alanına yüklenebilir; mevcut kayıtlar değiştirilmedi.");
            var companyExists = await db.Firmalar.AnyAsync(x => x.Id == firmaId && x.AktifMi, ct);
            if (!companyExists) throw new BackupException("Hedef firma bulunamadı veya pasif.");
            var targetUsers = await db.Kullanicilar.AsNoTracking().Where(x => x.FirmaId == firmaId).ToListAsync(ct);
            var userMap = new Dictionary<int, int>();
            foreach (var user in package.Users)
            {
                if (string.IsNullOrWhiteSpace(user.Username)) continue;
                var matches = targetUsers.Where(x => string.Equals(x.KullaniciAdi, user.Username, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Email, user.Email, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length == 1) userMap[user.Id] = matches[0].Id;
            }
            var maps = tables.ToDictionary(x => x.Type.Name, _ => new Dictionary<int, int>());
            int records = 0;
            foreach (var table in tables)
            {
                var inserted = new List<(int SourceId, object Entity)>();
                foreach (var row in package.Tables[table.Type.Name])
                {
                    var entity = Activator.CreateInstance(table.Type)!;
                    foreach (var property in Properties(table).Where(x => x.Name != "Id"))
                    {
                        var json = row[property.Name];
                        object? value = json.Deserialize(property.ClrType, JsonOptions);
                        if (property.Name == "FirmaId") value = firmaId;
                        else if (HasValue(json) && Links.TryGetValue(property.Name, out var parent))
                            value = maps[parent][json.GetInt32()];
                        else if (HasValue(json) && UserProperties.Contains(property.Name))
                        {
                            if (userMap.TryGetValue(json.GetInt32(), out var userId)) value = userId;
                            else if (property.IsNullable) value = null;
                            else throw new BackupException("Yedekteki kullanıcı hedef firmada bulunamadı. Aynı kullanıcı adı ve e-posta ile hesabı hazırlayın; hiçbir veri yüklenmedi.");
                        }
                        if (value is DateTime date) value = date.Kind == DateTimeKind.Local ? date.ToUniversalTime() : DateTime.SpecifyKind(date, DateTimeKind.Utc);
                        var document = package.Documents.SingleOrDefault(x => x.Table == table.Type.Name && x.RowId == Id(row) && x.Property == property.Name);
                        if (document != null) value = $"/uploads/yedek/{restoreId}/{Path.GetFileName(document.Entry)}";
                        property.PropertyInfo!.SetValue(entity, value);
                    }
                    db.Add(entity);
                    inserted.Add((Id(row), entity));
                }
                await db.SaveChangesAsync(ct);
                foreach (var (sourceId, entity) in inserted)
                    maps[table.Type.Name][sourceId] = (int)table.Type.GetProperty("Id")!.GetValue(entity)!;
                records += inserted.Count;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(final)!);
            Directory.Move(stage, final);
            await transaction.CommitAsync(ct);
            committed = true;
            var warnings = package.Warnings.ToList();
            if (package.Users.Any(x => !userMap.ContainsKey(x.Id)))
                warnings.Add("Hedefte eşleşmeyen eski kullanıcıların isteğe bağlı hesap bağlantıları boş bırakıldı; kayıtlı kullanıcı adları korundu.");
            return new RestoreResult(records, package.Documents.Count, warnings);
        }
        finally
        {
            if (!committed)
            {
                db.ChangeTracker.Clear();
                if (Directory.Exists(final)) Directory.Delete(final, true);
            }
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }

    public static async Task CopyLimitedAsync(Stream source, Stream destination, long limit, CancellationToken ct)
    {
        var bytes = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(bytes, ct)) > 0)
        {
            total += read;
            if (total > limit) throw new BackupException("Yedek veya belge boyut sınırını aşıyor.");
            await destination.WriteAsync(bytes.AsMemory(0, read), ct);
        }
    }
}
