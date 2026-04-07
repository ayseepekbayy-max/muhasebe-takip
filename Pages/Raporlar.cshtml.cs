using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using ClosedXML.Excel;
using System.IO;

namespace MuhasebeTakip2.App.Pages;

public class RaporlarModel : PageModel
{
    private readonly AppDbContext _db;
    public RaporlarModel(AppDbContext db) => _db = db;

    public decimal BugunGiris { get; set; }
    public decimal BugunCikis { get; set; }
    public decimal AyGiris { get; set; }
    public decimal AyCikis { get; set; }
    public decimal KasaBakiye { get; set; }

    public int CariSayisi { get; set; }
    public int AliciSayisi { get; set; }
    public int SaticiSayisi { get; set; }
    public int CalisanSayisi { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        BugunGiris = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId &&
                        x.Tarih >= today &&
                        x.Tarih < tomorrow &&
                        x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        BugunCikis = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId &&
                        x.Tarih >= today &&
                        x.Tarih < tomorrow &&
                        x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        AyGiris = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId &&
                        x.Tarih >= monthStart &&
                        x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        AyCikis = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId &&
                        x.Tarih >= monthStart &&
                        x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var toplamGiris = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId && x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var toplamCikis = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId && x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        KasaBakiye = toplamGiris - toplamCikis;

        CariSayisi = await _db.CariKartlar.CountAsync(x => x.FirmaId == firmaId);
        AliciSayisi = await _db.CariKartlar.CountAsync(x => x.FirmaId == firmaId && x.Tip == CariTip.Alici);
        SaticiSayisi = await _db.CariKartlar.CountAsync(x => x.FirmaId == firmaId && x.Tip == CariTip.Satici);

        CalisanSayisi = await _db.Calisanlar.CountAsync(x => x.FirmaId == firmaId);

        return Page();
    }

    public async Task<IActionResult> OnPostDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var bugunGiris = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId &&
                        x.Tarih >= today &&
                        x.Tarih < tomorrow &&
                        x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var bugunCikis = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId &&
                        x.Tarih >= today &&
                        x.Tarih < tomorrow &&
                        x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var ayGiris = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId &&
                        x.Tarih >= monthStart &&
                        x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var ayCikis = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId &&
                        x.Tarih >= monthStart &&
                        x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var toplamGiris = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId && x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var toplamCikis = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId && x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        var kasaBakiye = toplamGiris - toplamCikis;

        var cariSayisi = await _db.CariKartlar.CountAsync(x => x.FirmaId == firmaId);
        var aliciSayisi = await _db.CariKartlar.CountAsync(x => x.FirmaId == firmaId && x.Tip == CariTip.Alici);
        var saticiSayisi = await _db.CariKartlar.CountAsync(x => x.FirmaId == firmaId && x.Tip == CariTip.Satici);
        var calisanSayisi = await _db.Calisanlar.CountAsync(x => x.FirmaId == firmaId);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Raporlar");

        ws.Cell(1, 1).Value = "Rapor";
        ws.Cell(1, 2).Value = "Değer";

        var header = ws.Range(1, 1, 1, 2);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        ws.Cell(2, 1).Value = "Bugün Giriş";
        ws.Cell(2, 2).Value = bugunGiris;

        ws.Cell(3, 1).Value = "Bugün Çıkış";
        ws.Cell(3, 2).Value = bugunCikis;

        ws.Cell(4, 1).Value = "Bu Ay Giriş";
        ws.Cell(4, 2).Value = ayGiris;

        ws.Cell(5, 1).Value = "Bu Ay Çıkış";
        ws.Cell(5, 2).Value = ayCikis;

        ws.Cell(6, 1).Value = "Kasa Bakiye (Toplam)";
        ws.Cell(6, 2).Value = kasaBakiye;

        ws.Cell(7, 1).Value = "Cari Sayısı";
        ws.Cell(7, 2).Value = cariSayisi;

        ws.Cell(8, 1).Value = "Alıcı Sayısı";
        ws.Cell(8, 2).Value = aliciSayisi;

        ws.Cell(9, 1).Value = "Satıcı Sayısı";
        ws.Cell(9, 2).Value = saticiSayisi;

        ws.Cell(10, 1).Value = "Çalışan Sayısı";
        ws.Cell(10, 2).Value = calisanSayisi;

        var numberRange = ws.Range(2, 2, 10, 2);
        numberRange.Style.NumberFormat.Format = "#,##0.00";

        ws.Columns().AdjustToContents();

        var fullRange = ws.Range(1, 1, 10, 2);
        fullRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        fullRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var dosyaAdi = $"raporlar_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            dosyaAdi
        );
    }
}