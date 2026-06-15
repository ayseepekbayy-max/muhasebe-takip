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

        StilUygula(ws);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{tur.ToLower()}_ice_aktarma_sablonu.xlsx");
    }

    public async Task<IActionResult> OnGetDisaAktarAsync(string tur = "Cari")
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        using var workbook = new XLWorkbook();

        if (tur.Equals("Stok", StringComparison.OrdinalIgnoreCase))
            await StoklariDisaAktarAsync(workbook, firmaId.Value);
        else if (tur.Equals("Fatura", StringComparison.OrdinalIgnoreCase))
            await FaturalariDisaAktarAsync(workbook, firmaId.Value);
        else if (tur.Equals("Kasa", StringComparison.OrdinalIgnoreCase))
            await KasaDisaAktarAsync(workbook, firmaId.Value);
        else
            await CarileriDisaAktarAsync(workbook, firmaId.Value);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{tur.ToLower()}_disa_aktarim_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
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

    private async Task CarileriDisaAktarAsync(XLWorkbook workbook, int firmaId)
    {
        var ws = workbook.Worksheets.Add("Cari Kartlar");
        ws.Cell(1, 1).Value = "Ünvan";
        ws.Cell(1, 2).Value = "Tip";
        ws.Cell(1, 3).Value = "Telefon";
        ws.Cell(1, 4).Value = "Vergi No";
        ws.Cell(1, 5).Value = "Satış Faturası";
        ws.Cell(1, 6).Value = "Tahsilat";
        ws.Cell(1, 7).Value = "Kalan Tahsilat";
        ws.Cell(1, 8).Value = "Alış Faturası";
        ws.Cell(1, 9).Value = "Ödeme";
        ws.Cell(1, 10).Value = "Kalan Ödeme";
        ws.Cell(1, 11).Value = "Net Bakiye";
        ws.Cell(1, 12).Value = "Durum";

        var liste = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.Unvan)
            .ToListAsync();

        var faturaOzetleri = await _db.Faturalar
            .Where(x => x.FirmaId == firmaId && x.CariKartId != null)
            .GroupBy(x => x.CariKartId!.Value)
            .Select(g => new
            {
                CariId = g.Key,
                Satis = g.Where(x => x.Tip == FaturaTipi.Satis).Sum(x => (decimal?)x.GenelToplam) ?? 0,
                Alis = g.Where(x => x.Tip == FaturaTipi.Alis).Sum(x => (decimal?)x.GenelToplam) ?? 0
            })
            .ToDictionaryAsync(x => x.CariId);

        var kasaOzetleri = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId && x.CariKartId != null)
            .GroupBy(x => x.CariKartId!.Value)
            .Select(g => new
            {
                CariId = g.Key,
                Tahsilat = g.Where(x => x.Tip == HareketTipi.Giris).Sum(x => (decimal?)x.Tutar) ?? 0,
                Odeme = g.Where(x => x.Tip == HareketTipi.Cikis).Sum(x => (decimal?)x.Tutar) ?? 0
            })
            .ToDictionaryAsync(x => x.CariId);

        var row = 2;
        foreach (var item in liste)
        {
            faturaOzetleri.TryGetValue(item.Id, out var fatura);
            kasaOzetleri.TryGetValue(item.Id, out var kasa);

            var satis = fatura?.Satis ?? 0;
            var alis = fatura?.Alis ?? 0;
            var tahsilat = kasa?.Tahsilat ?? 0;
            var odeme = kasa?.Odeme ?? 0;
            var kalanTahsilat = Math.Max(0, satis - tahsilat);
            var kalanOdeme = Math.Max(0, alis - odeme);
            var netBakiye = kalanTahsilat - kalanOdeme;

            ws.Cell(row, 1).Value = item.Unvan;
            ws.Cell(row, 2).Value = item.Tip == CariTip.Alici ? "Alıcı" : "Satıcı";
            ws.Cell(row, 3).Value = item.Telefon ?? "";
            ws.Cell(row, 4).Value = item.VergiNo ?? "";
            ws.Cell(row, 5).Value = satis;
            ws.Cell(row, 6).Value = tahsilat;
            ws.Cell(row, 7).Value = kalanTahsilat;
            ws.Cell(row, 8).Value = alis;
            ws.Cell(row, 9).Value = odeme;
            ws.Cell(row, 10).Value = kalanOdeme;
            ws.Cell(row, 11).Value = netBakiye;
            ws.Cell(row, 12).Value = netBakiye > 0 ? "Alacak" : netBakiye < 0 ? "Borç" : "Kapalı";
            row++;
        }

        if (row > 2)
        {
            ws.Range(2, 5, row - 1, 11).Style.NumberFormat.Format = "#,##0.00";
            ws.Range(1, 1, row - 1, 12).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Range(1, 1, row - 1, 12).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        StilUygula(ws);
    }
    private async Task StoklariDisaAktarAsync(XLWorkbook workbook, int firmaId)
    {
        var ws = workbook.Worksheets.Add("Stoklar");
        ws.Cell(1, 1).Value = "Ürün Adı";
        ws.Cell(1, 2).Value = "Kod";
        ws.Cell(1, 3).Value = "Birim";
        ws.Cell(1, 4).Value = "Mevcut Stok";

        var liste = await _db.StokUrunler.Where(x => x.FirmaId == firmaId).OrderBy(x => x.Ad).ToListAsync();
        var hareketler = await _db.StokHareketleri.Where(x => x.FirmaId == firmaId).GroupBy(x => x.StokUrunId).Select(g => new
        {
            UrunId = g.Key,
            Giris = g.Where(x => x.Tip == StokHareketTipi.Giris).Sum(x => (decimal?)x.Miktar) ?? 0,
            Cikis = g.Where(x => x.Tip == StokHareketTipi.Cikis).Sum(x => (decimal?)x.Miktar) ?? 0
        }).ToListAsync();
        var stoklar = hareketler.ToDictionary(x => x.UrunId, x => x.Giris - x.Cikis);

        var row = 2;
        foreach (var item in liste)
        {
            ws.Cell(row, 1).Value = item.Ad;
            ws.Cell(row, 2).Value = item.Kod;
            ws.Cell(row, 3).Value = item.Birim;
            ws.Cell(row, 4).Value = stoklar.TryGetValue(item.Id, out var stok) ? stok : 0;
            row++;
        }
        StilUygula(ws);
    }

    private async Task FaturalariDisaAktarAsync(XLWorkbook workbook, int firmaId)
    {
        var ws = workbook.Worksheets.Add("Faturalar");
        ws.Cell(1, 1).Value = "Tarih";
        ws.Cell(1, 2).Value = "Fatura No";
        ws.Cell(1, 3).Value = "Tip";
        ws.Cell(1, 4).Value = "Cari";
        ws.Cell(1, 5).Value = "Genel Toplam";
        ws.Cell(1, 6).Value = "Ödenen";
        ws.Cell(1, 7).Value = "Kalan";

        var liste = await _db.Faturalar.Include(x => x.CariKart).Where(x => x.FirmaId == firmaId).OrderByDescending(x => x.Tarih).ToListAsync();
        var row = 2;
        foreach (var item in liste)
        {
            ws.Cell(row, 1).Value = item.Tarih;
            ws.Cell(row, 2).Value = item.FaturaNo;
            ws.Cell(row, 3).Value = item.Tip == FaturaTipi.Satis ? "Satış" : "Alış";
            ws.Cell(row, 4).Value = item.CariKart?.Unvan ?? "";
            ws.Cell(row, 5).Value = item.GenelToplam;
            ws.Cell(row, 6).Value = item.OdenenToplam;
            ws.Cell(row, 7).Value = item.KalanTutar;
            row++;
        }
        StilUygula(ws);
    }

    private async Task KasaDisaAktarAsync(XLWorkbook workbook, int firmaId)
    {
        var ws = workbook.Worksheets.Add("Kasa");
        ws.Cell(1, 1).Value = "Tarih";
        ws.Cell(1, 2).Value = "Tip";
        ws.Cell(1, 3).Value = "Tutar";
        ws.Cell(1, 4).Value = "Cari";
        ws.Cell(1, 5).Value = "Açıklama";

        var liste = await _db.KasaHareketleri.Include(x => x.CariKart).Where(x => x.FirmaId == firmaId).OrderByDescending(x => x.Tarih).ToListAsync();
        var row = 2;
        foreach (var item in liste)
        {
            ws.Cell(row, 1).Value = item.Tarih;
            ws.Cell(row, 2).Value = item.Tip == HareketTipi.Giris ? "Giriş" : "Çıkış";
            ws.Cell(row, 3).Value = item.Tutar;
            ws.Cell(row, 4).Value = item.CariKart?.Unvan ?? "";
            ws.Cell(row, 5).Value = item.Aciklama;
            row++;
        }
        StilUygula(ws);
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

    private static void StilUygula(IXLWorksheet ws)
    {
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
        ws.Columns().AdjustToContents();
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