using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Araclar;

public class YedeklemeModel : PageModel
{
    private readonly AppDbContext _db;

    public YedeklemeModel(AppDbContext db)
    {
        _db = db;
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

        var cariler = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId.Value && x.AktifMi)
            .OrderBy(x => x.Unvan)
            .Select(x => new CariYedek(x.Unvan, x.Ad, x.Telefon, x.VergiNo, x.Tip, x.OlusturmaTarihi))
            .ToListAsync();

        var stoklar = await _db.StokUrunler
            .Where(x => x.FirmaId == firmaId.Value && x.AktifMi)
            .OrderBy(x => x.Ad)
            .Select(x => new StokYedek(x.Ad, x.Kod, x.Birim))
            .ToListAsync();

        var faturalar = await _db.Faturalar
            .Include(x => x.CariKart)
            .Include(x => x.Kalemler)
            .Where(x => x.FirmaId == firmaId.Value && x.AktifMi)
            .OrderBy(x => x.Tarih)
            .Select(x => new FaturaYedek(
                x.FaturaNo,
                x.CariKart != null ? x.CariKart.Unvan : null,
                x.Tip,
                x.Tarih,
                x.VadeTarihi,
                x.AraToplam,
                x.KdvToplam,
                x.GenelToplam,
                x.OdenenToplam,
                x.Aciklama,
                x.Kalemler.Select(k => new FaturaKalemYedek(k.Aciklama, k.Miktar, k.BirimFiyat, k.KdvOrani, k.AraToplam, k.KdvTutar, k.GenelToplam)).ToList(),
                x.Durum))
            .ToListAsync();

        var kasa = await _db.KasaHareketleri
            .Include(x => x.CariKart)
            .Where(x => x.FirmaId == firmaId.Value)
            .OrderBy(x => x.Tarih)
            .Select(x => new KasaYedek(x.Tarih, x.Tip, x.Tutar, x.Aciklama, x.CariKart != null ? x.CariKart.Unvan : null))
            .ToListAsync();

        var cekler = await _db.Cekler
            .Where(x => x.FirmaId == firmaId.Value)
            .OrderBy(x => x.Tarih)
            .Select(x => new CekYedek(x.No, x.Tarih, x.Tutar, x.Aciklama, x.Tip))
            .ToListAsync();

        var paket = new YedekPaketi
        {
            OlusturmaTarihi = DateTime.UtcNow,
            Cariler = cariler,
            Stoklar = stoklar,
            Faturalar = faturalar,
            KasaHareketleri = kasa,
            Cekler = cekler
        };

        var json = JsonSerializer.Serialize(paket, new JsonSerializerOptions { WriteIndented = true });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        return File(bytes, "application/json", $"firmova_yedek_{DateTime.Now:yyyyMMdd_HHmmss}.json");
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
            using var stream = YedekDosyasi.OpenReadStream();
            var paket = await JsonSerializer.DeserializeAsync<YedekPaketi>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (paket == null)
            {
                Hata = "Yedek dosyası okunamadı.";
                return Page();
            }

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

                var cariId = string.IsNullOrWhiteSpace(fatura.CariUnvan)
                    ? null
                    : await _db.CariKartlar.Where(x => x.FirmaId == firmaId.Value && x.Unvan == fatura.CariUnvan).Select(x => (int?)x.Id).FirstOrDefaultAsync();

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
            Mesaj = $"Güvenli geri yükleme tamamlandı. Eklenen ana kayıt: {eklenen}. Mevcut kayıtlar korunup tekrar eklenmedi.";
        }
        catch (Exception ex)
        {
            Hata = $"Geri yükleme sırasında hata oluştu: {ex.Message}";
        }

        return Page();
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
