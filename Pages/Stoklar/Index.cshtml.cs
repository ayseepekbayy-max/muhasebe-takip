using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using ClosedXML.Excel;
using System.IO;

namespace MuhasebeTakip2.App.Pages.Stoklar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db) => _db = db;

    public List<StokUrun> Liste { get; set; } = new();
    public Dictionary<int, decimal> Stoklar { get; set; } = new();
    public Dictionary<int, decimal> SonBirimFiyatlar { get; set; } = new();
    public Dictionary<int, decimal> SonKoliFiyatlar { get; set; } = new();
    public Dictionary<int, decimal> StokDegerleri { get; set; } = new();

    [BindProperty]
    [ValidateNever]
    public StokUrun Yeni { get; set; } = new() { Birim = "Adet" };

    [BindProperty]
    public decimal Miktar { get; set; }

    [BindProperty]
    public decimal BirimFiyat { get; set; }

    [BindProperty]
    public decimal KoliFiyat { get; set; }

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await ListeyiYukleAsync(firmaId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostEkleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Yeni.Ad = (Yeni.Ad ?? "").Trim();
        Yeni.Kod = (Yeni.Kod ?? "").Trim();
        Yeni.Birim = (Yeni.Birim ?? "").Trim();

        if (string.IsNullOrWhiteSpace(Yeni.Ad))
        {
            Hata = "Ürün adı boş olamaz.";
            await ListeyiYukleAsync(firmaId.Value);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Yeni.Birim))
            Yeni.Birim = "Adet";

        if (Miktar < 0)
        {
            Hata = "Miktar 0'dan küçük olamaz.";
            await ListeyiYukleAsync(firmaId.Value);
            return Page();
        }

        if (BirimFiyat < 0 || KoliFiyat < 0)
        {
            Hata = "Fiyat alanları 0'dan küçük olamaz.";
            await ListeyiYukleAsync(firmaId.Value);
            return Page();
        }

        Yeni.FirmaId = firmaId.Value;

        _db.StokUrunler.Add(Yeni);
        await _db.SaveChangesAsync();

        if (Miktar > 0)
        {
            _db.StokHareketleri.Add(new StokHareket
            {
                FirmaId = firmaId.Value,
                StokUrunId = Yeni.Id,
                Tarih = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc),
                Tip = StokHareketTipi.Giris,
                Miktar = Miktar,
                BirimFiyat = BirimFiyat,
                KoliFiyat = KoliFiyat,
                Aciklama = "İlk stok girişi"
            });

            await _db.SaveChangesAsync();
        }

        TempData["Basari"] = "Ürün eklendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var urun = await _db.StokUrunler
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.FirmaId == firmaId.Value);

        if (urun == null)
        {
            TempData["Hata"] = "Ürün bulunamadı.";
            return RedirectToPage();
        }

        var hareketler = await _db.StokHareketleri
            .Where(x =>
                x.StokUrunId == id &&
                x.FirmaId == firmaId.Value)
            .ToListAsync();

        if (hareketler.Any())
            _db.StokHareketleri.RemoveRange(hareketler);

        _db.StokUrunler.Remove(urun);
        await _db.SaveChangesAsync();

        TempData["Basari"] = "Ürün ve varsa ona ait stok hareketleri silindi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await ListeyiYukleAsync(firmaId.Value);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Stoklar");

        ws.Cell(1, 1).Value = "Ürün Adı";
        ws.Cell(1, 2).Value = "Kod";
        ws.Cell(1, 3).Value = "Birim";
        ws.Cell(1, 4).Value = "Mevcut Stok";
        ws.Cell(1, 5).Value = "Birim Fiyat";
        ws.Cell(1, 6).Value = "Koli Fiyatı";
        ws.Cell(1, 7).Value = "Stok Değeri";

        var header = ws.Range(1, 1, 1, 7);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        header.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        header.Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        int row = 2;

        foreach (var s in Liste)
        {
            ws.Cell(row, 1).Value = s.Ad ?? "";
            ws.Cell(row, 2).Value = s.Kod ?? "";
            ws.Cell(row, 3).Value = s.Birim ?? "";
            ws.Cell(row, 4).Value = Stoklar.ContainsKey(s.Id) ? Stoklar[s.Id] : 0;
            ws.Cell(row, 5).Value = SonBirimFiyatlar.ContainsKey(s.Id) ? SonBirimFiyatlar[s.Id] : 0;
            ws.Cell(row, 6).Value = SonKoliFiyatlar.ContainsKey(s.Id) ? SonKoliFiyatlar[s.Id] : 0;
            ws.Cell(row, 7).Value = StokDegerleri.ContainsKey(s.Id) ? StokDegerleri[s.Id] : 0;
            ws.Range(row, 4, row, 7).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }

        ws.Columns().AdjustToContents();

        if (row > 2)
        {
            var range = ws.Range(1, 1, row - 1, 7);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var dosyaAdi = $"stoklar_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            dosyaAdi
        );
    }

    private async Task ListeyiYukleAsync(int firmaId)
    {
        Liste = await _db.StokUrunler
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.Ad)
            .ToListAsync();

        var urunIdleri = Liste.Select(x => x.Id).ToList();

        var hareketler = await _db.StokHareketleri
            .Where(x =>
                x.FirmaId == firmaId &&
                urunIdleri.Contains(x.StokUrunId))
            .GroupBy(x => x.StokUrunId)
            .Select(g => new
            {
                UrunId = g.Key,
                Giris = g.Where(x => x.Tip == StokHareketTipi.Giris).Sum(x => (decimal?)x.Miktar) ?? 0,
                Cikis = g.Where(x => x.Tip == StokHareketTipi.Cikis).Sum(x => (decimal?)x.Miktar) ?? 0
            })
            .ToListAsync();

        Stoklar = Liste.ToDictionary(x => x.Id, _ => 0m);
        SonBirimFiyatlar = Liste.ToDictionary(x => x.Id, _ => 0m);
        SonKoliFiyatlar = Liste.ToDictionary(x => x.Id, _ => 0m);
        StokDegerleri = Liste.ToDictionary(x => x.Id, _ => 0m);

        foreach (var h in hareketler)
            Stoklar[h.UrunId] = h.Giris - h.Cikis;

        var sonGirisler = await _db.StokHareketleri
            .Where(x =>
                x.FirmaId == firmaId &&
                urunIdleri.Contains(x.StokUrunId) &&
                x.Tip == StokHareketTipi.Giris)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        foreach (var grup in sonGirisler.GroupBy(x => x.StokUrunId))
        {
            var son = grup.First();
            SonBirimFiyatlar[son.StokUrunId] = son.BirimFiyat;
            SonKoliFiyatlar[son.StokUrunId] = son.KoliFiyat;
            StokDegerleri[son.StokUrunId] = Stoklar[son.StokUrunId] * son.BirimFiyat;
        }
    }
}