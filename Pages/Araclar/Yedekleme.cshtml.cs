using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Araclar;

[RequestSizeLimit(AccountingBackupService.MaxPackageBytes + 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = AccountingBackupService.MaxPackageBytes)]
public class YedeklemeModel : PageModel
{
    private readonly AppDbContext _db;

    private readonly AccountingBackupService _backup;
    private readonly ILogger<YedeklemeModel> _logger;

    public YedeklemeModel(AppDbContext db, IWebHostEnvironment environment, ILogger<YedeklemeModel> logger)
    {
        _db = db;
        _backup = new AccountingBackupService(db, environment);
        _logger = logger;
    }

    [BindProperty]
    public IFormFile? YedekDosyasi { get; set; }

    public string Mesaj { get; set; } = "";
    public string Hata { get; set; } = "";

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetInt32("FirmaId") == null)
            return RedirectToPage("/Login");

        return Page();
    }

    public async Task<IActionResult> OnGetIndirAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        try
        {
            var bytes = await _backup.ExportAsync(firmaId.Value, HttpContext.RequestAborted);
            return File(bytes, "application/zip", $"firmova_tam_yedek_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        }
        catch (AccountingBackupService.BackupException ex) { Hata = ex.Message; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firma {FirmaId} yedeği oluşturulamadı.", firmaId);
            Hata = "Yedek oluşturulamadı. Eksik belge veya kayıt bağlantılarını kontrol edin; eksiksiz yedek dosyası üretilemedi.";
        }
        return Page();
    }

    public async Task<IActionResult> OnPostGeriYukleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        if (YedekDosyasi == null || YedekDosyasi.Length == 0)
        {
            Hata = "Lütfen bir yedek dosyası seçin.";
            return Page();
        }

        try
        {
            if (YedekDosyasi.Length > AccountingBackupService.MaxPackageBytes)
                throw new AccountingBackupService.BackupException("Yedek dosyası en fazla 100 MB olabilir.");
            await using var source = YedekDosyasi.OpenReadStream();
            using var stream = new MemoryStream();
            await AccountingBackupService.CopyLimitedAsync(source, stream, AccountingBackupService.MaxPackageBytes, HttpContext.RequestAborted);
            stream.Position = 0;
            if (stream.Length >= 4 && stream.GetBuffer()[0] == (byte)'P' && stream.GetBuffer()[1] == (byte)'K')
            {
                var result = await _backup.RestoreAsync(stream, firmaId.Value, HttpContext.RequestAborted);
                Mesaj = $"Tam yedek geri yüklendi: {result.Records} kayıt, {result.Files} belge. " + string.Join(" ", result.Warnings);
                return Page();
            }
            if (stream.Length > AccountingBackupService.MaxManifestBytes)
                throw new AccountingBackupService.BackupException("Eski JSON yedek dosyası 32 MB sınırını aşıyor.");
            using (var document = await JsonDocument.ParseAsync(stream))
            {
                var names = document.RootElement.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!new[] { "OlusturmaTarihi", "Cariler", "Stoklar", "Faturalar", "KasaHareketleri", "Cekler" }.All(names.Contains))
                    throw new AccountingBackupService.BackupException("Desteklenen eski yedek alanları bulunamadı.");
            }
            stream.Position = 0;
            var paket = await JsonSerializer.DeserializeAsync<YedekPaketi>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (paket == null)
            {
                Hata = "Yedek dosyası okunamadı.";
                return Page();
            }

            if (paket.Cariler == null || paket.Stoklar == null || paket.Faturalar == null || paket.KasaHareketleri == null || paket.Cekler == null ||
                paket.Cariler.GroupBy(x => x.Unvan).Any(g => g.Count() > 1) ||
                paket.Faturalar.GroupBy(x => x.FaturaNo).Any(g => g.Count() > 1))
                throw new AccountingBackupService.BackupException("Eski yedekte boş liste veya belirsiz yinelenen kayıt var.");
            await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var eklenen = 0;

            foreach (var cari in paket.Cariler)
            {
                if (string.IsNullOrWhiteSpace(cari.Unvan))
                    continue;

                var varMi = await _db.CariKartlar.AnyAsync(x => x.FirmaId == firmaId.Value && x.Unvan == cari.Unvan);
                if (varMi)
                    continue;

                _db.CariKartlar.Add(new CariKart
                {
                    FirmaId = firmaId.Value,
                    Unvan = cari.Unvan,
                    Ad = string.IsNullOrWhiteSpace(cari.Ad) ? cari.Unvan : cari.Ad,
                    Telefon = cari.Telefon,
                    VergiNo = cari.VergiNo,
                    Tip = cari.Tip,
                    AktifMi = true,
                    OlusturmaTarihi = cari.OlusturmaTarihi == default ? DateTime.UtcNow : cari.OlusturmaTarihi
                });
                eklenen++;
            }

            foreach (var stok in paket.Stoklar)
            {
                if (string.IsNullOrWhiteSpace(stok.Ad))
                    continue;

                var varMi = await _db.StokUrunler.AnyAsync(x => x.FirmaId == firmaId.Value && x.Ad == stok.Ad && x.Kod == stok.Kod);
                if (varMi)
                    continue;

                _db.StokUrunler.Add(new StokUrun
                {
                    FirmaId = firmaId.Value,
                    Ad = stok.Ad,
                    Kod = stok.Kod ?? "",
                    Birim = string.IsNullOrWhiteSpace(stok.Birim) ? "Adet" : stok.Birim,
                    AktifMi = true
                });
                eklenen++;
            }

            await _db.SaveChangesAsync();

            foreach (var fatura in paket.Faturalar)
            {
                if (string.IsNullOrWhiteSpace(fatura.FaturaNo))
                    continue;

                var varMi = await _db.Faturalar.AnyAsync(x => x.FirmaId == firmaId.Value && x.FaturaNo == fatura.FaturaNo);
                if (varMi)
                    continue;

                var cariId = string.IsNullOrWhiteSpace(fatura.CariUnvan) ? null
                    : await LegacyCariIdAsync(fatura.CariUnvan, firmaId.Value);

                _db.Faturalar.Add(new Fatura
                {
                    FirmaId = firmaId.Value,
                    CariKartId = cariId,
                    FaturaNo = fatura.FaturaNo,
                    Tip = fatura.Tip,
                    Tarih = DateTime.SpecifyKind(fatura.Tarih.Date, DateTimeKind.Utc),
                    VadeTarihi = fatura.VadeTarihi.HasValue ? DateTime.SpecifyKind(fatura.VadeTarihi.Value.Date, DateTimeKind.Utc) : null,
                    AraToplam = fatura.AraToplam,
                    KdvToplam = fatura.KdvToplam,
                    GenelToplam = fatura.GenelToplam,
                    OdenenToplam = fatura.OdenenToplam,
                    Durum = fatura.Durum ?? FaturaDurumuExtensions.OdemeDurumu(fatura.GenelToplam, fatura.OdenenToplam),
                    Aciklama = fatura.Aciklama ?? "",
                    AktifMi = true,
                    OlusturmaTarihi = DateTime.UtcNow,
                    Kalemler = fatura.Kalemler.Select(k => new FaturaKalem
                    {
                        Aciklama = k.Aciklama ?? "",
                        Miktar = k.Miktar,
                        BirimFiyat = k.BirimFiyat,
                        KdvOrani = k.KdvOrani,
                        AraToplam = k.AraToplam,
                        KdvTutar = k.KdvTutar,
                        GenelToplam = k.GenelToplam
                    }).ToList()
                });
                eklenen++;
            }

            // Old format has no invoice IDs on cash rows: restore amounts without guessing links.
            await _db.SaveChangesAsync();
            var existingCash = await _db.KasaHareketleri.AsNoTracking()
                .Where(x => x.FirmaId == firmaId.Value).ToListAsync();
            var usedCash = new HashSet<int>();
            foreach (var kasa in paket.KasaHareketleri)
            {
                var cariId = string.IsNullOrWhiteSpace(kasa.CariUnvan) ? null
                    : await LegacyCariIdAsync(kasa.CariUnvan, firmaId.Value);
                var tarih = DateTime.SpecifyKind(kasa.Tarih, DateTimeKind.Utc);
                var existing = existingCash.FirstOrDefault(x => !usedCash.Contains(x.Id) && x.Tarih == tarih &&
                    x.Tip == kasa.Tip && x.Tutar == kasa.Tutar && x.CariKartId == cariId && x.Aciklama == (kasa.Aciklama ?? ""));
                if (existing != null) { usedCash.Add(existing.Id); continue; }
                _db.KasaHareketleri.Add(new KasaHareket
                {
                    FirmaId = firmaId.Value, CariKartId = cariId, Tarih = tarih,
                    Tip = kasa.Tip, Tutar = kasa.Tutar, Aciklama = kasa.Aciklama ?? ""
                });
                eklenen++;
            }

            foreach (var cek in paket.Cekler)
            {
                var varMi = await _db.Cekler.AnyAsync(x => x.FirmaId == firmaId.Value && x.No == cek.No && x.Tarih == cek.Tarih && x.Tutar == cek.Tutar);
                if (varMi)
                    continue;

                _db.Cekler.Add(new Cek
                {
                    FirmaId = firmaId.Value,
                    No = cek.No ?? "",
                    Tarih = DateTime.SpecifyKind(cek.Tarih.Date, DateTimeKind.Utc),
                    Tutar = cek.Tutar,
                    Aciklama = cek.Aciklama ?? "",
                    Tip = cek.Tip,
                    OlusturmaTarihi = DateTime.UtcNow
                });
                eklenen++;
            }

            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
            Mesaj = $"Eski JSON yedeği yüklendi. Eklenen kayıt: {eklenen}. Mevcut kayıtlar korundu. " +
                "Bu eski format eksik kapsamlıdır: personel, stok hareketleri, ödeme planları, maliyetler ve belge dosyaları içermez. " +
                "Kasa hareketlerinin fatura bağlantıları eski dosyada bulunmadığından otomatik eşleştirilmedi; eski kayıtları ayrıca kontrol edin.";
        }
        catch (AccountingBackupService.BackupException ex)
        {
            _db.ChangeTracker.Clear();
            Hata = ex.Message;
        }
        catch (Exception ex)
        {
            _db.ChangeTracker.Clear();
            _logger.LogError(ex, "Firma {FirmaId} yedeği geri yüklenemedi.", firmaId);
            Hata = "Yedek geri yüklenemedi. Dosya bozuk, eksik veya bu sürümle uyumsuz olabilir. Hiçbir kayıt eklenmedi.";
        }

        return Page();
    }

    private async Task<int?> LegacyCariIdAsync(string unvan, int firmaId)
    {
        var matches = await _db.CariKartlar.Where(x => x.FirmaId == firmaId && x.Unvan == unvan)
            .Select(x => x.Id).Take(2).ToListAsync();
        if (matches.Count != 1)
            throw new AccountingBackupService.BackupException("Eski yedekte bir cari bağlantısı eksik veya belirsiz. İşlem geri alındı.");
        return matches[0];
    }

    public class YedekPaketi
    {
        public DateTime OlusturmaTarihi { get; set; }
        public List<CariYedek> Cariler { get; set; } = new();
        public List<StokYedek> Stoklar { get; set; } = new();
        public List<FaturaYedek> Faturalar { get; set; } = new();
        public List<KasaYedek> KasaHareketleri { get; set; } = new();
        public List<CekYedek> Cekler { get; set; } = new();
    }

    public record CariYedek(string Unvan, string Ad, string? Telefon, string? VergiNo, CariTip Tip, DateTime OlusturmaTarihi);
    public record StokYedek(string Ad, string? Kod, string? Birim);
    public record FaturaYedek(string FaturaNo, string? CariUnvan, FaturaTipi Tip, DateTime Tarih, DateTime? VadeTarihi, decimal AraToplam, decimal KdvToplam, decimal GenelToplam, decimal OdenenToplam, string? Aciklama, List<FaturaKalemYedek> Kalemler, FaturaDurumu? Durum = null);
    public record FaturaKalemYedek(string? Aciklama, decimal Miktar, decimal BirimFiyat, decimal KdvOrani, decimal AraToplam, decimal KdvTutar, decimal GenelToplam);
    public record KasaYedek(DateTime Tarih, HareketTipi Tip, decimal Tutar, string? Aciklama, string? CariUnvan);
    public record CekYedek(string? No, DateTime Tarih, decimal Tutar, string? Aciklama, CekTipi Tip);
}
