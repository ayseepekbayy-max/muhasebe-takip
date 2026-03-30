using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using ClosedXML.Excel;
using System.IO;

namespace MuhasebeTakip2.App.Pages.CariKartlar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<CariKart> Alicilar { get; set; } = new();
    public List<CariKart> Saticilar { get; set; } = new();

    [BindProperty]
    public CariKart YeniCari { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Alicilar = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId && x.Tip == CariTip.Alici)
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        Saticilar = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId && x.Tip == CariTip.Satici)
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        if (YeniCari.Tip == 0) YeniCari.Tip = CariTip.Alici;

        return Page();
    }

    public async Task<IActionResult> OnPostEkleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        if (string.IsNullOrWhiteSpace(YeniCari.Unvan))
        {
            ModelState.AddModelError("", "Ünvan zorunludur.");

            Alicilar = await _db.CariKartlar
                .Where(x => x.FirmaId == firmaId && x.Tip == CariTip.Alici)
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            Saticilar = await _db.CariKartlar
                .Where(x => x.FirmaId == firmaId && x.Tip == CariTip.Satici)
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            return Page();
        }

        YeniCari.Unvan = YeniCari.Unvan.Trim();
        YeniCari.Telefon = (YeniCari.Telefon ?? "").Trim();
        YeniCari.VergiNo = (YeniCari.VergiNo ?? "").Trim();
        YeniCari.FirmaId = firmaId.Value;

        _db.CariKartlar.Add(YeniCari);
        await _db.SaveChangesAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var cari = await _db.CariKartlar
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (cari != null)
        {
            _db.CariKartlar.Remove(cari);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var cariler = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.Unvan)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Cari Kartlar");

        ws.Cell(1, 1).Value = "Ünvan";
        ws.Cell(1, 2).Value = "Telefon";
        ws.Cell(1, 3).Value = "Vergi No";
        ws.Cell(1, 4).Value = "Tip";

        var header = ws.Range(1, 1, 1, 4);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        int row = 2;
        foreach (var c in cariler)
        {
            ws.Cell(row, 1).Value = c.Unvan ?? "";
            ws.Cell(row, 2).Value = c.Telefon ?? "";
            ws.Cell(row, 3).Value = c.VergiNo ?? "";
            ws.Cell(row, 4).Value = c.Tip.ToString();
            row++;
        }

        ws.Columns().AdjustToContents();

        if (row > 2)
        {
            var range = ws.Range(1, 1, row - 1, 4);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var dosyaAdi = $"cari_kartlar_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            dosyaAdi
        );
    }
}