using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using ClosedXML.Excel;
using System.IO;

namespace MuhasebeTakip2.App.Pages.Musteriler;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Musteri> Liste { get; set; } = new();

    [BindProperty]
    [ValidateNever]
    public Musteri Yeni { get; set; } = new();

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Liste = await _db.Musteriler
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.AdSoyad)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostEkleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Yeni.AdSoyad = (Yeni.AdSoyad ?? "").Trim();
        Yeni.Telefon = (Yeni.Telefon ?? "").Trim();
        Yeni.Adres = (Yeni.Adres ?? "").Trim();

        if (string.IsNullOrWhiteSpace(Yeni.AdSoyad))
        {
            Hata = "Ad Soyad boş olamaz.";

            Liste = await _db.Musteriler
                .Where(x => x.FirmaId == firmaId)
                .OrderBy(x => x.AdSoyad)
                .ToListAsync();

            return Page();
        }

        Yeni.FirmaId = firmaId.Value;

        _db.Musteriler.Add(Yeni);
        await _db.SaveChangesAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        ModelState.Clear();

        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var m = await _db.Musteriler
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (m == null)
            return RedirectToPage();

        var isVar = await _db.MusteriIsler
            .AnyAsync(x => x.MusteriId == id && x.FirmaId == firmaId);

        if (isVar)
        {
            Hata = "Bu müşteriye bağlı iş kaydı olduğu için silinemez.";

            Liste = await _db.Musteriler
                .Where(x => x.FirmaId == firmaId)
                .OrderBy(x => x.AdSoyad)
                .ToListAsync();

            return Page();
        }

        _db.Musteriler.Remove(m);
        await _db.SaveChangesAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var musteriler = await _db.Musteriler
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.AdSoyad)
            .ToListAsync();

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Müşteriler");

        ws.Cell(1, 1).Value = "Ad Soyad";
        ws.Cell(1, 2).Value = "Telefon";
        ws.Cell(1, 3).Value = "Adres";

        var header = ws.Range(1, 1, 1, 3);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        int row = 2;
        foreach (var m in musteriler)
        {
            ws.Cell(row, 1).Value = m.AdSoyad ?? "";
            ws.Cell(row, 2).Value = m.Telefon ?? "";
            ws.Cell(row, 3).Value = m.Adres ?? "";
            row++;
        }

        ws.Columns().AdjustToContents();

        if (row > 2)
        {
            var range = ws.Range(1, 1, row - 1, 3);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var dosyaAdi = $"musteriler_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            dosyaAdi
        );
    }
}