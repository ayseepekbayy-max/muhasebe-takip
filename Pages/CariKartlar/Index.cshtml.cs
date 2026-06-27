using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.CariKartlar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public IndexModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public List<CariOzet> Cariler { get; set; } = new();
    public int ToplamCari { get; set; }
    public int AliciSayisi { get; set; }
    public int SaticiSayisi { get; set; }
    public decimal ToplamBakiye { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UnvanAra { get; set; }

    [BindProperty(SupportsGet = true)]
    public CariTip? TipFiltre { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? IletisimAra { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DuzenleId { get; set; }

    [BindProperty]
    [ValidateNever]
    public CariKart YeniCari { get; set; } = new();

    [BindProperty]
    [ValidateNever]
    public CariDuzenleForm DuzenlenenCari { get; set; } = new();

    public bool DuzenlemeAcik { get; set; }
    public bool YeniCariModalAcik { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await ListeleriYukleAsync(firmaId.Value);

        if (DuzenleId.HasValue)
        {
            var cari = await _db.CariKartlar
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == DuzenleId.Value && x.FirmaId == firmaId.Value && x.AktifMi);

            if (cari != null)
            {
                DuzenlemeAcik = true;
                DuzenlenenCari = new CariDuzenleForm
                {
                    Id = cari.Id,
                    Unvan = cari.Unvan,
                    Tip = cari.Tip,
                    Telefon = cari.Telefon,
                    VergiNo = cari.VergiNo
                };
            }
        }

        if ((int)YeniCari.Tip == 0)
            YeniCari.Tip = CariTip.Alici;

        return Page();
    }

    public async Task<IActionResult> OnPostEkleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        YeniCari.Unvan = (YeniCari.Unvan ?? "").Trim();
        YeniCari.Telefon = Temizle(YeniCari.Telefon);
        YeniCari.VergiNo = Temizle(YeniCari.VergiNo);

        if (string.IsNullOrWhiteSpace(YeniCari.Unvan))
        {
            ModelState.AddModelError("", "Ünvan zorunludur.");
            YeniCariModalAcik = true;
            await ListeleriYukleAsync(firmaId.Value);
            return Page();
        }

        if (!Enum.IsDefined(YeniCari.Tip))
            YeniCari.Tip = CariTip.Alici;

        YeniCari.Ad = YeniCari.Unvan;
        YeniCari.FirmaId = firmaId.Value;
        YeniCari.OlusturmaTarihi = DateTime.UtcNow;
        YeniCari.AktifMi = true;
        YeniCari.ArsivTarihi = null;
        YeniCari.ArsivNotu = null;

        try
        {
            _db.CariKartlar.Add(YeniCari);
            await _db.SaveChangesWithAuditAsync(
                () => _islemGecmisi.KaydetAsync(
                    "Cari Kartlar",
                    "Ekleme",
                    $"{YeniCari.Unvan} ünvanlı cari kart eklendi (ID: {YeniCari.Id}).",
                    yeniDeger: IslemGecmisiSnapshots.Cari(YeniCari)),
                anaKaydiOnceKaydet: true);

            TempData["Basari"] = "Cari kart eklendi.";
            return RedirectToPage();
        }
        catch (DbUpdateException ex)
        {
            ModelState.AddModelError("", $"Veritabanı hatası: {ex.InnerException?.Message ?? ex.Message}");
            YeniCariModalAcik = true;
            await ListeleriYukleAsync(firmaId.Value);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDuzenleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var cari = await _db.CariKartlar
            .FirstOrDefaultAsync(x => x.Id == DuzenlenenCari.Id && x.FirmaId == firmaId.Value && x.AktifMi);

        if (cari == null)
        {
            TempData["Hata"] = "Cari kart bulunamadı.";
            return RedirectToPage();
        }

        DuzenlenenCari.Unvan = (DuzenlenenCari.Unvan ?? "").Trim();
        DuzenlenenCari.Telefon = Temizle(DuzenlenenCari.Telefon);
        DuzenlenenCari.VergiNo = Temizle(DuzenlenenCari.VergiNo);

        if (string.IsNullOrWhiteSpace(DuzenlenenCari.Unvan))
        {
            ModelState.AddModelError("", "Ünvan zorunludur.");
            DuzenlemeAcik = true;
            await ListeleriYukleAsync(firmaId.Value);
            return Page();
        }

        if (!Enum.IsDefined(DuzenlenenCari.Tip))
            DuzenlenenCari.Tip = CariTip.Alici;

        var eskiDeger = IslemGecmisiSnapshots.Cari(cari);

        cari.Ad = DuzenlenenCari.Unvan;
        cari.Unvan = DuzenlenenCari.Unvan;
        cari.Tip = DuzenlenenCari.Tip;
        cari.Telefon = DuzenlenenCari.Telefon;
        cari.VergiNo = DuzenlenenCari.VergiNo;

        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Cari Kartlar",
                "Düzenleme",
                $"{cari.Unvan} ünvanlı cari kart düzenlendi (ID: {cari.Id}).",
                eskiDeger,
                IslemGecmisiSnapshots.Cari(cari)),
            anaKaydiOnceKaydet: false);

        TempData["Basari"] = "Cari kart güncellendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var cari = await _db.CariKartlar
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value && x.AktifMi);

        if (cari == null)
        {
            TempData["Hata"] = "Cari kart bulunamadı.";
            return RedirectToPage();
        }

        var eskiDeger = IslemGecmisiSnapshots.Cari(cari);
        cari.AktifMi = false;
        cari.ArsivTarihi = DateTime.UtcNow;
        cari.ArsivNotu = "Silme yerine veri korunarak arşive taşındı.";

        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Cari Kartlar",
                "Arsivleme",
                $"{cari.Unvan} cari karti silinmeden arsive tasindi (ID: {cari.Id}).",
                eskiDeger,
                IslemGecmisiSnapshots.Cari(cari)),
            anaKaydiOnceKaydet: false);

        TempData["Basari"] = "Cari kart silinmeden arsive tasindi. Bagli kayitlar korundu.";
        return RedirectToPage();

    }

    public async Task<IActionResult> OnGetDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var cariler = await CariOzetSorgusu(firmaId.Value)
            .OrderBy(x => x.Unvan)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Cari Kartlar");
        var basliklar = new[]
        {
            "Ünvan", "Tip", "Telefon", "Vergi No", "Toplam Tahsilat",
            "Toplam Ödeme", "Fatura Sayısı", "Toplam Fatura Tutarı"
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
        foreach (var cari in cariler)
        {
            ws.Cell(row, 1).Value = cari.Unvan;
            ws.Cell(row, 2).Value = cari.Tip == CariTip.Alici ? "Alıcı" : "Satıcı";
            ws.Cell(row, 3).Value = cari.Telefon ?? "";
            ws.Cell(row, 4).Value = cari.VergiNo ?? "";
            ws.Cell(row, 5).Value = cari.ToplamTahsilat;
            ws.Cell(row, 6).Value = cari.ToplamOdeme;
            ws.Cell(row, 7).Value = cari.FaturaSayisi;
            ws.Cell(row, 8).Value = cari.ToplamFaturaTutari;
            row++;
        }

        ws.Columns(5, 6).Style.NumberFormat.Format = "#,##0.00 ₺";
        ws.Column(8).Style.NumberFormat.Format = "#,##0.00 ₺";
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
            $"cari_kartlar_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    private async Task ListeleriYukleAsync(int firmaId)
    {
        var cariSayilari = await _db.CariKartlar
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.AktifMi)
            .GroupBy(_ => 1)
            .Select(grup => new
            {
                Toplam = grup.Count(),
                Alici = grup.Count(x => x.Tip == CariTip.Alici),
                Satici = grup.Count(x => x.Tip == CariTip.Satici)
            })
            .FirstOrDefaultAsync();

        ToplamCari = cariSayilari?.Toplam ?? 0;
        AliciSayisi = cariSayilari?.Alici ?? 0;
        SaticiSayisi = cariSayilari?.Satici ?? 0;

        var kasaOzeti = await _db.KasaHareketleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.CariKartId != null)
            .GroupBy(_ => 1)
            .Select(grup => new
            {
                Tahsilat = grup.Where(x => x.Tip == HareketTipi.Giris).Sum(x => (decimal?)x.Tutar) ?? 0,
                Odeme = grup.Where(x => x.Tip == HareketTipi.Cikis).Sum(x => (decimal?)x.Tutar) ?? 0
            })
            .FirstOrDefaultAsync();

        ToplamBakiye = (kasaOzeti?.Tahsilat ?? 0) - (kasaOzeti?.Odeme ?? 0);

        Cariler = await CariOzetSorgusu(firmaId)
            .OrderByDescending(x => x.Id)
            .ToListAsync();
    }

    private IQueryable<CariOzet> CariOzetSorgusu(int firmaId)
    {
        var sorgu = _db.CariKartlar
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.AktifMi);

        if (!string.IsNullOrWhiteSpace(UnvanAra))
        {
            var unvan = UnvanAra.Trim();
            sorgu = sorgu.Where(x => x.Unvan.Contains(unvan));
        }

        if (TipFiltre.HasValue)
            sorgu = sorgu.Where(x => x.Tip == TipFiltre.Value);

        if (!string.IsNullOrWhiteSpace(IletisimAra))
        {
            var iletisim = IletisimAra.Trim();
            sorgu = sorgu.Where(x =>
                (x.Telefon != null && x.Telefon.Contains(iletisim)) ||
                (x.VergiNo != null && x.VergiNo.Contains(iletisim)));
        }

        return sorgu.Select(cari => new CariOzet
        {
            Id = cari.Id,
            Unvan = cari.Unvan,
            Tip = cari.Tip,
            Telefon = cari.Telefon,
            VergiNo = cari.VergiNo,
            ToplamTahsilat = _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId && x.CariKartId == cari.Id && x.Tip == HareketTipi.Giris)
                .Sum(x => (decimal?)x.Tutar) ?? 0,
            ToplamOdeme = _db.KasaHareketleri
                .Where(x => x.FirmaId == firmaId && x.CariKartId == cari.Id && x.Tip == HareketTipi.Cikis)
                .Sum(x => (decimal?)x.Tutar) ?? 0,
            FaturaSayisi = _db.Faturalar
                .Count(x => x.FirmaId == firmaId && x.CariKartId == cari.Id),
            ToplamFaturaTutari = _db.Faturalar
                .Where(x => x.FirmaId == firmaId && x.CariKartId == cari.Id && x.Durum != FaturaDurumu.Iptal)
                .Sum(x => (decimal?)x.GenelToplam) ?? 0
        });
    }

    private static string? Temizle(string? deger) =>
        string.IsNullOrWhiteSpace(deger) ? null : deger.Trim();

    public class CariOzet
    {
        public int Id { get; set; }
        public string Unvan { get; set; } = "";
        public CariTip Tip { get; set; }
        public string? Telefon { get; set; }
        public string? VergiNo { get; set; }
        public decimal ToplamTahsilat { get; set; }
        public decimal ToplamOdeme { get; set; }
        public int FaturaSayisi { get; set; }
        public decimal ToplamFaturaTutari { get; set; }
    }

    public class CariDuzenleForm
    {
        public int Id { get; set; }
        public string Unvan { get; set; } = "";
        public CariTip Tip { get; set; }
        public string? Telefon { get; set; }
        public string? VergiNo { get; set; }
    }
}
