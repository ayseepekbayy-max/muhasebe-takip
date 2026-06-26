using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Calisanlar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public IndexModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public List<CalisanOzet> Liste { get; set; } = new();

    public int ToplamCalisan { get; set; }
    public int AktifCalisan { get; set; }
    public decimal BuAyMaas { get; set; }
    public decimal BuAyAvans { get; set; }
    public decimal BekleyenMaas { get; set; }
    public int BuAyDevamsizlik { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? AdSoyadAra { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TelefonAra { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? DurumFiltre { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? TarihBaslangic { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? TarihBitis { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DuzenleId { get; set; }

    [BindProperty]
    [ValidateNever]
    public Calisan YeniCalisan { get; set; } = new();

    [BindProperty]
    [ValidateNever]
    public CalisanDuzenleForm DuzenlenenCalisan { get; set; } = new();

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";
    public bool YeniCalisanModalAcik { get; set; }
    public bool DuzenlemeModalAcik { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await ListeyiYukleAsync(firmaId.Value);

        if (DuzenleId.HasValue)
        {
            var calisan = await _db.Calisanlar
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == DuzenleId.Value && x.FirmaId == firmaId.Value);

            if (calisan != null)
            {
                DuzenlemeModalAcik = true;
                DuzenlenenCalisan = new CalisanDuzenleForm
                {
                    Id = calisan.Id,
                    AdSoyad = calisan.AdSoyad,
                    Telefon = calisan.Telefon,
                    Maas = calisan.Maas,
                    IseGirisTarihi = calisan.IseGirisTarihi.Date
                };
            }
        }

        if (YeniCalisan.IseGirisTarihi == default)
            YeniCalisan.IseGirisTarihi = DateTime.UtcNow.Date;

        return Page();
    }

    public async Task<IActionResult> OnPostEkleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        YeniCalisan.AdSoyad = (YeniCalisan.AdSoyad ?? "").Trim();
        YeniCalisan.Telefon = (YeniCalisan.Telefon ?? "").Trim();

        if (string.IsNullOrWhiteSpace(YeniCalisan.AdSoyad))
        {
            ModelState.AddModelError("", "Ad Soyad zorunludur.");
            YeniCalisanModalAcik = true;
            await ListeyiYukleAsync(firmaId.Value);
            return Page();
        }

        if (YeniCalisan.Maas < 0) YeniCalisan.Maas = 0;
        if (YeniCalisan.Avans < 0) YeniCalisan.Avans = 0;

        YeniCalisan.Ad = YeniCalisan.AdSoyad;
        YeniCalisan.FirmaId = firmaId.Value;
        YeniCalisan.AktifMi = true;
        YeniCalisan.AyrilisTarihi = null;
        YeniCalisan.AyrilisNotu = null;
        YeniCalisan.IseGirisTarihi = TarihiUtcYap(YeniCalisan.IseGirisTarihi == default
            ? DateTime.UtcNow.Date
            : YeniCalisan.IseGirisTarihi);

        _db.Calisanlar.Add(YeniCalisan);
        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Çalışanlar",
                "Ekleme",
                $"{YeniCalisan.AdSoyad} çalışanı eklendi (ID: {YeniCalisan.Id}).",
                yeniDeger: IslemGecmisiSnapshots.Calisan(YeniCalisan)),
            anaKaydiOnceKaydet: true);

        TempData["Basari"] = "Çalışan eklendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDuzenleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var calisan = await _db.Calisanlar
            .FirstOrDefaultAsync(x => x.Id == DuzenlenenCalisan.Id && x.FirmaId == firmaId.Value);

        if (calisan == null)
        {
            TempData["Hata"] = "Çalışan bulunamadı.";
            return RedirectToPage();
        }

        DuzenlenenCalisan.AdSoyad = (DuzenlenenCalisan.AdSoyad ?? "").Trim();
        DuzenlenenCalisan.Telefon = (DuzenlenenCalisan.Telefon ?? "").Trim();

        if (string.IsNullOrWhiteSpace(DuzenlenenCalisan.AdSoyad))
        {
            ModelState.AddModelError("", "Ad Soyad zorunludur.");
            DuzenlemeModalAcik = true;
            await ListeyiYukleAsync(firmaId.Value);
            return Page();
        }

        if (DuzenlenenCalisan.Maas < 0)
            DuzenlenenCalisan.Maas = 0;

        var eskiDeger = IslemGecmisiSnapshots.Calisan(calisan);

        calisan.Ad = DuzenlenenCalisan.AdSoyad;
        calisan.AdSoyad = DuzenlenenCalisan.AdSoyad;
        calisan.Telefon = DuzenlenenCalisan.Telefon;
        calisan.Maas = DuzenlenenCalisan.Maas;
        calisan.IseGirisTarihi = TarihiUtcYap(DuzenlenenCalisan.IseGirisTarihi == default
            ? DateTime.UtcNow.Date
            : DuzenlenenCalisan.IseGirisTarihi);

        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Çalışanlar",
                "Düzenleme",
                $"{calisan.AdSoyad} çalışanı düzenlendi (ID: {calisan.Id}).",
                eskiDeger,
                IslemGecmisiSnapshots.Calisan(calisan)),
            anaKaydiOnceKaydet: false);

        TempData["Basari"] = "Çalışan güncellendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostArsivleAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var calisan = await _db.Calisanlar
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (calisan == null)
            return RedirectToPage();

        var eskiDeger = IslemGecmisiSnapshots.Calisan(calisan);

        calisan.AktifMi = false;
        calisan.AyrilisTarihi = DateTime.UtcNow;
        calisan.AyrilisNotu = "Çalışan aktif listeden arşive taşındı.";

        await _islemGecmisi.KaydetAsync(
            "Çalışanlar",
            "Düzenleme",
            $"{calisan.AdSoyad} çalışanı arşive taşındı.",
            eskiDeger,
            IslemGecmisiSnapshots.Calisan(calisan));
        await _db.SaveChangesAsync();

        TempData["Basari"] = "Çalışan ayrıldı olarak işaretlendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var calisan = await _db.Calisanlar
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (calisan == null)
            return RedirectToPage();

        var avanslar = await _db.CalisanAvanslari
            .Where(x => x.CalisanId == id && x.FirmaId == firmaId)
            .ToListAsync();

        var puantajlar = await _db.CalisanPuantajlari
            .Where(x => x.CalisanId == id && x.FirmaId == firmaId)
            .ToListAsync();

        var maasArsivleri = await _db.CalisanMaasArsivleri
            .Where(x => x.CalisanId == id && x.FirmaId == firmaId)
            .ToListAsync();

        if (avanslar.Count > 0)
            _db.CalisanAvanslari.RemoveRange(avanslar);

        if (puantajlar.Count > 0)
            _db.CalisanPuantajlari.RemoveRange(puantajlar);

        if (maasArsivleri.Count > 0)
            _db.CalisanMaasArsivleri.RemoveRange(maasArsivleri);

        var eskiDeger = IslemGecmisiSnapshots.Calisan(calisan);
        _db.Calisanlar.Remove(calisan);

        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Çalışanlar",
                "Silme",
                $"{calisan.AdSoyad} çalışanı silindi (ID: {calisan.Id}).",
                eskiDeger: eskiDeger),
            anaKaydiOnceKaydet: false);

        TempData["Basari"] = "Çalışan silindi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await ListeyiYukleAsync(firmaId.Value);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Çalışanlar");
        var basliklar = new[]
        {
            "Ad Soyad", "Telefon", "Maaş", "Son Avans", "Son Maaş",
            "Bu Ay Avans", "Bu Ay Maaş", "İşe Giriş Tarihi", "Durum"
        };

        for (var i = 0; i < basliklar.Length; i++)
            ws.Cell(1, i + 1).Value = basliklar[i];

        var header = ws.Range(1, 1, 1, basliklar.Length);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        var row = 2;
        foreach (var c in Liste)
        {
            ws.Cell(row, 1).Value = c.AdSoyad;
            ws.Cell(row, 2).Value = c.Telefon ?? "";
            ws.Cell(row, 3).Value = c.Maas;
            ws.Cell(row, 4).Value = c.SonAvans;
            ws.Cell(row, 5).Value = c.SonMaas;
            ws.Cell(row, 6).Value = c.BuAyAvans;
            ws.Cell(row, 7).Value = c.BuAyMaas;
            ws.Cell(row, 8).Value = c.IseGirisTarihi;
            ws.Cell(row, 9).Value = c.AktifMi ? "Aktif" : "Ayrıldı";
            row++;
        }

        ws.Columns(3, 7).Style.NumberFormat.Format = "#,##0.00 ₺";
        ws.Column(8).Style.DateFormat.Format = "dd.MM.yyyy";
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        if (row > 2)
        {
            var range = ws.Range(1, 1, row - 1, basliklar.Length);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"calisanlar_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    public async Task<IActionResult> OnPostDisaAktarAsync() => await OnGetDisaAktarAsync();

    private async Task ListeyiYukleAsync(int firmaId)
    {
        var bugun = DateTime.UtcNow.Date;
        var ayBaslangic = new DateTime(bugun.Year, bugun.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var ayBitis = ayBaslangic.AddMonths(1);

        var sayilar = await _db.Calisanlar
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Toplam = g.Count(),
                Aktif = g.Count(x => x.AktifMi),
                AylikMaas = g.Where(x => x.AktifMi).Sum(x => (decimal?)x.Maas) ?? 0
            })
            .FirstOrDefaultAsync();

        ToplamCalisan = sayilar?.Toplam ?? 0;
        AktifCalisan = sayilar?.Aktif ?? 0;

        var hareketOzeti = await _db.CalisanAvanslari
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.Tarih >= ayBaslangic && x.Tarih < ayBitis)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Maas = g.Where(x => x.Tip == CalisanHareketTipi.MaasOdeme || x.Tip == CalisanHareketTipi.Diger)
                    .Sum(x => (decimal?)x.Tutar) ?? 0,
                Avans = g.Where(x => x.Tip == CalisanHareketTipi.Avans)
                    .Sum(x => (decimal?)x.Tutar) ?? 0
            })
            .FirstOrDefaultAsync();

        BuAyMaas = hareketOzeti?.Maas ?? 0;
        BuAyAvans = hareketOzeti?.Avans ?? 0;
        BekleyenMaas = Math.Max(0, (sayilar?.AylikMaas ?? 0) - BuAyMaas);

        BuAyDevamsizlik = await _db.CalisanPuantajlari
            .AsNoTracking()
            .CountAsync(x =>
                x.FirmaId == firmaId &&
                x.Tarih >= ayBaslangic &&
                x.Tarih < ayBitis &&
                x.Durum == PuantajDurum.Gelmedi);

        var sorgu = _db.Calisanlar
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.AktifMi && x.AyrilisTarihi == null);

        if (!string.IsNullOrWhiteSpace(AdSoyadAra))
        {
            var ad = AdSoyadAra.Trim();
            sorgu = sorgu.Where(x => x.AdSoyad.Contains(ad));
        }

        if (!string.IsNullOrWhiteSpace(TelefonAra))
        {
            var telefon = TelefonAra.Trim();
            sorgu = sorgu.Where(x => x.Telefon != null && x.Telefon.Contains(telefon));
        }

        if (DurumFiltre == "Aktif")
            sorgu = sorgu.Where(x => x.AktifMi);
        else if (DurumFiltre == "Ayrildi")
            sorgu = sorgu.Where(x => !x.AktifMi);

        if (TarihBaslangic.HasValue)
        {
            var baslangic = TarihiUtcYap(TarihBaslangic.Value);
            sorgu = sorgu.Where(x => x.IseGirisTarihi >= baslangic);
        }

        if (TarihBitis.HasValue)
        {
            var bitis = TarihiUtcYap(TarihBitis.Value).AddDays(1);
            sorgu = sorgu.Where(x => x.IseGirisTarihi < bitis);
        }

        Liste = await sorgu
            .Select(calisan => new CalisanOzet
            {
                Id = calisan.Id,
                AdSoyad = calisan.AdSoyad,
                Telefon = calisan.Telefon,
                Maas = calisan.Maas,
                IseGirisTarihi = calisan.IseGirisTarihi,
                AktifMi = calisan.AktifMi,
                AyrilisTarihi = calisan.AyrilisTarihi,
                SonAvans = _db.CalisanAvanslari
                    .Where(x => x.FirmaId == firmaId && x.CalisanId == calisan.Id && x.Tip == CalisanHareketTipi.Avans)
                    .OrderByDescending(x => x.Tarih)
                    .ThenByDescending(x => x.Id)
                    .Select(x => (decimal?)x.Tutar)
                    .FirstOrDefault() ?? 0,
                SonMaas = _db.CalisanAvanslari
                    .Where(x => x.FirmaId == firmaId && x.CalisanId == calisan.Id &&
                                (x.Tip == CalisanHareketTipi.MaasOdeme || x.Tip == CalisanHareketTipi.Diger))
                    .OrderByDescending(x => x.Tarih)
                    .ThenByDescending(x => x.Id)
                    .Select(x => (decimal?)x.Tutar)
                    .FirstOrDefault() ?? 0,
                SonAvansTarihi = _db.CalisanAvanslari
                    .Where(x => x.FirmaId == firmaId && x.CalisanId == calisan.Id && x.Tip == CalisanHareketTipi.Avans)
                    .OrderByDescending(x => x.Tarih)
                    .ThenByDescending(x => x.Id)
                    .Select(x => (DateTime?)x.Tarih)
                    .FirstOrDefault(),
                SonMaasTarihi = _db.CalisanAvanslari
                    .Where(x => x.FirmaId == firmaId && x.CalisanId == calisan.Id &&
                                (x.Tip == CalisanHareketTipi.MaasOdeme || x.Tip == CalisanHareketTipi.Diger))
                    .OrderByDescending(x => x.Tarih)
                    .ThenByDescending(x => x.Id)
                    .Select(x => (DateTime?)x.Tarih)
                    .FirstOrDefault(),
                BuAyAvans = _db.CalisanAvanslari
                    .Where(x => x.FirmaId == firmaId && x.CalisanId == calisan.Id &&
                                x.Tip == CalisanHareketTipi.Avans &&
                                x.Tarih >= ayBaslangic && x.Tarih < ayBitis)
                    .Sum(x => (decimal?)x.Tutar) ?? 0,
                BuAyMaas = _db.CalisanAvanslari
                    .Where(x => x.FirmaId == firmaId && x.CalisanId == calisan.Id &&
                                (x.Tip == CalisanHareketTipi.MaasOdeme || x.Tip == CalisanHareketTipi.Diger) &&
                                x.Tarih >= ayBaslangic && x.Tarih < ayBitis)
                    .Sum(x => (decimal?)x.Tutar) ?? 0
            })
            .OrderByDescending(x => x.AktifMi)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
    }

    private static DateTime TarihiUtcYap(DateTime tarih)
    {
        var sadeceTarih = tarih.Date;
        return tarih.Kind switch
        {
            DateTimeKind.Utc => sadeceTarih,
            DateTimeKind.Local => tarih.ToUniversalTime().Date,
            _ => DateTime.SpecifyKind(sadeceTarih, DateTimeKind.Utc)
        };
    }

    public class CalisanOzet
    {
        public int Id { get; set; }
        public string AdSoyad { get; set; } = "";
        public string? Telefon { get; set; }
        public decimal Maas { get; set; }
        public decimal SonAvans { get; set; }
        public decimal SonMaas { get; set; }
        public decimal BuAyAvans { get; set; }
        public decimal BuAyMaas { get; set; }
        public DateTime? SonAvansTarihi { get; set; }
        public DateTime? SonMaasTarihi { get; set; }
        public DateTime IseGirisTarihi { get; set; }
        public bool AktifMi { get; set; }
        public DateTime? AyrilisTarihi { get; set; }
        public string BasHarf => string.IsNullOrWhiteSpace(AdSoyad) ? "Ç" : AdSoyad.Trim()[0].ToString().ToUpperInvariant();
    }

    public class CalisanDuzenleForm
    {
        public int Id { get; set; }
        public string AdSoyad { get; set; } = "";
        public string? Telefon { get; set; }
        public decimal Maas { get; set; }
        public DateTime IseGirisTarihi { get; set; } = DateTime.UtcNow.Date;
    }
}
