using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using ClosedXML.Excel;
using System.IO;

namespace MuhasebeTakip2.App.Pages.Calisanlar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<Calisan> Liste { get; set; } = new();

    [BindProperty]
    public Calisan YeniCalisan { get; set; } = new();

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Liste = await _db.Calisanlar
            .Where(x => x.FirmaId == firmaId && x.AktifMi)
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostEkleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        if (string.IsNullOrWhiteSpace(YeniCalisan.AdSoyad))
        {
            Hata = "Ad Soyad zorunludur.";

            Liste = await _db.Calisanlar
                .Where(x => x.FirmaId == firmaId && x.AktifMi)
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            return Page();
        }

        YeniCalisan.AdSoyad = YeniCalisan.AdSoyad.Trim();
        YeniCalisan.Telefon = (YeniCalisan.Telefon ?? "").Trim();

        if (YeniCalisan.Maas < 0) YeniCalisan.Maas = 0;
        if (YeniCalisan.Avans < 0) YeniCalisan.Avans = 0;

        YeniCalisan.FirmaId = firmaId.Value;
        YeniCalisan.AktifMi = true;
        YeniCalisan.AyrilisTarihi = null;
        YeniCalisan.AyrilisNotu = null;

        if (YeniCalisan.IseGirisTarihi == default)
        {
            YeniCalisan.IseGirisTarihi = DateTime.UtcNow;
        }
        else
        {
            var t = YeniCalisan.IseGirisTarihi;
            YeniCalisan.IseGirisTarihi = t.Kind switch
            {
                DateTimeKind.Utc => t,
                DateTimeKind.Local => t.ToUniversalTime(),
                _ => DateTime.SpecifyKind(t, DateTimeKind.Utc)
            };
        }

        _db.Calisanlar.Add(YeniCalisan);
        await _db.SaveChangesAsync();

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

        calisan.AktifMi = false;
        calisan.AyrilisTarihi = DateTime.UtcNow;
        calisan.AyrilisNotu = "Çalışan aktif listeden arşive taşındı.";

        await _db.SaveChangesAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var calisanlar = await _db.Calisanlar
            .Where(x => x.FirmaId == firmaId && x.AktifMi)
            .OrderBy(x => x.AdSoyad)
            .ToListAsync();

        var calisanIdleri = calisanlar.Select(x => x.Id).ToList();

        var hareketOzetleri = await _db.CalisanAvanslari
            .Where(x => x.FirmaId == firmaId && calisanIdleri.Contains(x.CalisanId))
            .GroupBy(x => x.CalisanId)
            .Select(g => new
            {
                CalisanId = g.Key,
                ToplamAvans = g
                    .Where(x => x.Tip == CalisanHareketTipi.Avans)
                    .Sum(x => (decimal?)x.Tutar) ?? 0,
                ToplamMaas = g
                    .Where(x => x.Tip == CalisanHareketTipi.MaasOdeme)
                    .Sum(x => (decimal?)x.Tutar) ?? 0
            })
            .ToListAsync();

        var ozetMap = hareketOzetleri.ToDictionary(
            x => x.CalisanId,
            x => new
            {
                x.ToplamAvans,
                x.ToplamMaas
            });

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Çalışanlar");

        ws.Cell(1, 1).Value = "Ad Soyad";
        ws.Cell(1, 2).Value = "Telefon";
        ws.Cell(1, 3).Value = "Maaş (Toplam)";
        ws.Cell(1, 4).Value = "Toplam Avans";
        ws.Cell(1, 5).Value = "Kalan (Maaş - Avans)";
        ws.Cell(1, 6).Value = "İşe Giriş Tarihi";

        var header = ws.Range(1, 1, 1, 6);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        int row = 2;
        foreach (var c in calisanlar)
        {
            var toplamAvans = ozetMap.ContainsKey(c.Id) ? ozetMap[c.Id].ToplamAvans : 0;
            var toplamMaas = ozetMap.ContainsKey(c.Id) ? ozetMap[c.Id].ToplamMaas : 0;
            var kalan = toplamMaas - toplamAvans;

            ws.Cell(row, 1).Value = c.AdSoyad ?? "";
            ws.Cell(row, 2).Value = c.Telefon ?? "";

            ws.Cell(row, 3).Value = toplamMaas;
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";

            ws.Cell(row, 4).Value = toplamAvans;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";

            ws.Cell(row, 5).Value = kalan;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";

            ws.Cell(row, 6).Value = c.IseGirisTarihi;
            ws.Cell(row, 6).Style.DateFormat.Format = "dd.MM.yyyy";

            row++;
        }

        ws.Columns().AdjustToContents();

        if (row > 2)
        {
            var range = ws.Range(1, 1, row - 1, 6);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var dosyaAdi = $"calisanlar_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            dosyaAdi
        );
    }
}