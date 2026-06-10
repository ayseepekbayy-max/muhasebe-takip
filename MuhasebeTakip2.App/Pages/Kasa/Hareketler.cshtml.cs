using ClosedXML.Excel;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Kasa;

public class HareketlerModel : PageModel
{
    private readonly AppDbContext _db;
    public HareketlerModel(AppDbContext db) => _db = db;

    public List<KasaHareket> Hareketler { get; set; } = new();

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Hareketler = await _db.KasaHareketleri
            .Include(x => x.CariKart)
            .Where(x => x.FirmaId == firmaId)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var h = await _db.KasaHareketleri
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (h == null)
        {
            Hata = "Silinecek hareket bulunamadı.";

            Hareketler = await _db.KasaHareketleri
                .Include(x => x.CariKart)
                .Where(x => x.FirmaId == firmaId)
                .OrderByDescending(x => x.Tarih)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return Page();
        }

        _db.KasaHareketleri.Remove(h);
        await _db.SaveChangesAsync();

        Mesaj = "Kasa hareketi silindi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var hareketler = await _db.KasaHareketleri
            .Include(x => x.CariKart)
            .Where(x => x.FirmaId == firmaId)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Kasa Hareketleri");

        ws.Cell(1, 1).Value = "Tarih";
        ws.Cell(1, 2).Value = "Tip";
        ws.Cell(1, 3).Value = "Tutar";
        ws.Cell(1, 4).Value = "Cari";
        ws.Cell(1, 5).Value = "Açıklama";

        var headerRange = ws.Range(1, 1, 1, 5);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightGray;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        headerRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        int row = 2;

        foreach (var h in hareketler)
        {
            ws.Cell(row, 1).Value = h.Tarih;
            ws.Cell(row, 1).Style.DateFormat.Format = "dd.MM.yyyy";

            ws.Cell(row, 2).Value = h.Tip.ToString();

            ws.Cell(row, 3).Value = h.Tutar;
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";

            ws.Cell(row, 4).Value = h.CariKart?.Unvan ?? "";
            ws.Cell(row, 5).Value = h.Aciklama ?? "";

            row++;
        }

        ws.Columns().AdjustToContents();

        if (row > 2)
        {
            var dataRange = ws.Range(1, 1, row - 1, 5);
            dataRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
            dataRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var dosyaAdi = $"kasa_hareketleri_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            dosyaAdi
        );
    }
}