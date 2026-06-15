using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Araclar;

public class ExcelIceAktarModel : PageModel
{
    private readonly AppDbContext _db;

    public ExcelIceAktarModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public IFormFile? Dosya { get; set; }

    [BindProperty]
    public string Tur { get; set; } = "Cari";

    public string Mesaj { get; set; } = "";
    public string Hata { get; set; } = "";

    public IActionResult OnGet()
    {
        if (HttpContext.Session.GetInt32("FirmaId") == null)
            return RedirectToPage("/Login");

        return Page();
    }

    public IActionResult OnGetSablon(string tur = "Cari")
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(tur.Equals("Stok", StringComparison.OrdinalIgnoreCase) ? "Stok" : "Cari");

        if (tur.Equals("Stok", StringComparison.OrdinalIgnoreCase))
        {
            ws.Cell(1, 1).Value = "Ürün Adı";
            ws.Cell(1, 2).Value = "Kod";
            ws.Cell(1, 3).Value = "Birim";
            ws.Cell(1, 4).Value = "Miktar";
            ws.Cell(1, 5).Value = "Birim Fiyat";
            ws.Cell(1, 6).Value = "KDV Oranı";
            ws.Cell(2, 1).Value = "Masa Takımı";
            ws.Cell(2, 2).Value = "STK-001";
            ws.Cell(2, 3).Value = "Adet";
            ws.Cell(2, 4).Value = 5;
            ws.Cell(2, 5).Value = 1250;
            ws.Cell(2, 6).Value = 20;
        }
        else
        {
            ws.Cell(1, 1).Value = "Ünvan";
            ws.Cell(1, 2).Value = "Tip";
            ws.Cell(1, 3).Value = "Telefon";
            ws.Cell(1, 4).Value = "Vergi No";
            ws.Cell(2, 1).Value = "Örnek Müşteri";
            ws.Cell(2, 2).Value = "Alıcı";
            ws.Cell(2, 3).Value = "0555 000 00 00";
            ws.Cell(2, 4).Value = "1234567890";
        }

        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{tur.ToLower()}_ice_aktarma_sablonu.xlsx");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        if (Dosya == null || Dosya.Length == 0)
        {
            Hata = "Lütfen bir Excel dosyası seçin.";
            return Page();
        }

        try
        {
            using var stream = Dosya.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var ws = workbook.Worksheets.First();

            var eklenen = Tur.Equals("Stok", StringComparison.OrdinalIgnoreCase)
                ? await StoklariIceAktarAsync(ws, firmaId.Value)
                : await CarileriIceAktarAsync(ws, firmaId.Value);

            Mesaj = $"İçe aktarma tamamlandı. Eklenen kayıt: {eklenen}.";
        }
        catch (Exception ex)
        {
            Hata = $"İçe aktarma sırasında hata oluştu: {ex.Message}";
        }

        return Page();
    }

    private async Task<int> CarileriIceAktarAsync(IXLWorksheet ws, int firmaId)
    {
        var eklenen = 0;
        var sonSatir = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (var row = 2; row <= sonSatir; row++)
        {
            var unvan = ws.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(unvan))
                continue;

            var varMi = await _db.CariKartlar.AnyAsync(x => x.FirmaId == firmaId && x.Unvan.ToLower() == unvan.ToLower());
            if (varMi)
                continue;

            var tipText = ws.Cell(row, 2).GetString().Trim().ToLower();
            var tip = tipText.Contains("sat") ? CariTip.Satici : CariTip.Alici;

            _db.CariKartlar.Add(new CariKart
            {
                FirmaId = firmaId,
                Unvan = unvan,
                Ad = unvan,
                Tip = tip,
                Telefon = BosMu(ws.Cell(row, 3).GetString()),
                VergiNo = BosMu(ws.Cell(row, 4).GetString()),
                OlusturmaTarihi = DateTime.UtcNow
            });
            eklenen++;
        }

        await _db.SaveChangesAsync();
        return eklenen;
    }

    private async Task<int> StoklariIceAktarAsync(IXLWorksheet ws, int firmaId)
    {
        var eklenen = 0;
        var sonSatir = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (var row = 2; row <= sonSatir; row++)
        {
            var ad = ws.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(ad))
                continue;

            var kod = ws.Cell(row, 2).GetString().Trim();
            var urun = await _db.StokUrunler.FirstOrDefaultAsync(x => x.FirmaId == firmaId &&
                ((!string.IsNullOrWhiteSpace(kod) && x.Kod == kod) || x.Ad.ToLower() == ad.ToLower()));

            if (urun == null)
            {
                urun = new StokUrun
                {
                    FirmaId = firmaId,
                    Ad = ad,
                    Kod = kod,
                    Birim = string.IsNullOrWhiteSpace(ws.Cell(row, 3).GetString()) ? "Adet" : ws.Cell(row, 3).GetString().Trim()
                };
                _db.StokUrunler.Add(urun);
                eklenen++;
            }

            var miktar = DecimalOku(ws.Cell(row, 4));
            if (miktar > 0)
            {
                _db.StokHareketleri.Add(new StokHareket
                {
                    FirmaId = firmaId,
                    StokUrun = urun,
                    Ad = ad,
                    Tarih = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc),
                    Tip = StokHareketTipi.Giris,
                    Miktar = miktar,
                    BirimFiyat = DecimalOku(ws.Cell(row, 5)),
                    KdvOrani = DecimalOku(ws.Cell(row, 6)),
                    Aciklama = "Excel içe aktarma"
                });
            }
        }

        await _db.SaveChangesAsync();
        return eklenen;
    }

    private static string? BosMu(string value)
    {
        value = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static decimal DecimalOku(IXLCell cell)
    {
        if (cell.TryGetValue<decimal>(out var value))
            return value;

        var text = cell.GetString().Replace(".", ",");
        return decimal.TryParse(text, out value) ? value : 0;
    }
}