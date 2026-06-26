using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Stoklar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public IndexModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public List<StokOzet> Liste { get; set; } = new();
    public List<string> BirimSecenekleri { get; set; } = new();
    public int ToplamUrunSayisi { get; set; }
    public int KritikStokSayisi { get; set; }
    public decimal ToplamStokDegeri { get; set; }
    public decimal BugunkuGirisMiktari { get; set; }
    public decimal BugunkuCikisMiktari { get; set; }
    public bool YeniUrunModalAcik { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? UrunAra { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool KritikFiltre { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? BirimFiltre { get; set; }

    [BindProperty]
    [ValidateNever]
    public StokUrun Yeni { get; set; } = new() { Birim = "Adet" };

    public string Hata { get; set; } = "";

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
            YeniUrunModalAcik = true;
            await ListeyiYukleAsync(firmaId.Value);
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Yeni.Birim))
            Yeni.Birim = "Adet";

        if (Yeni.MinStokSeviyesi < 0)
        {
            Hata = "Minimum stok seviyesi 0'dan küçük olamaz.";
            YeniUrunModalAcik = true;
            await ListeyiYukleAsync(firmaId.Value);
            return Page();
        }

        Yeni.FirmaId = firmaId.Value;

        _db.StokUrunler.Add(Yeni);
        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Stok",
                "Ekleme",
                $"{Yeni.Ad} stok ürünü eklendi (ID: {Yeni.Id}).",
                yeniDeger: IslemGecmisiSnapshots.StokUrun(Yeni)),
            anaKaydiOnceKaydet: true);

        TempData["Basari"] = "Ürün kartı eklendi. Stok girişi için Detay sayfasını kullanabilirsiniz.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var urun = await _db.StokUrunler
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);

        if (urun == null)
        {
            TempData["Hata"] = "Ürün bulunamadı.";
            return RedirectToPage();
        }

        var hareketler = await _db.StokHareketleri
            .Where(x => x.StokUrunId == id && x.FirmaId == firmaId.Value)
            .ToListAsync();

        if (hareketler.Count > 0)
        {
            TempData["Hata"] = "Bu urune ait stok hareketleri var. Gecmis kayitlarin korunmasi icin urun silinmedi.";
            return RedirectToPage();
        }

        var eskiDeger = IslemGecmisiSnapshots.StokUrun(urun);
        _db.StokUrunler.Remove(urun);

        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Stok",
                "Silme",
                $"{urun.Ad} stok ürünü ve {hareketler.Count} stok hareketi silindi (ID: {urun.Id}).",
                eskiDeger: eskiDeger),
            anaKaydiOnceKaydet: false);

        TempData["Basari"] = "Ürün ve varsa ona ait stok hareketleri silindi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await ListeyiYukleAsync(firmaId.Value);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Stoklar");
        var basliklar = new[]
        {
            "Ürün Adı", "Kod", "Birim", "Minimum Stok", "Mevcut Stok",
            "Kritik Durum", "Son Birim Fiyat", "Son KDV Oranı",
            "KDV Dahil Birim Fiyat", "Stok Değeri"
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
        foreach (var stok in Liste)
        {
            ws.Cell(row, 1).Value = stok.Ad;
            ws.Cell(row, 2).Value = stok.Kod;
            ws.Cell(row, 3).Value = stok.Birim;
            ws.Cell(row, 4).Value = stok.MinStokSeviyesi;
            ws.Cell(row, 5).Value = stok.MevcutStok;
            ws.Cell(row, 6).Value = stok.KritikMi ? "Kritik" : "Normal";
            ws.Cell(row, 7).Value = stok.SonBirimFiyat;
            ws.Cell(row, 8).Value = stok.SonKdvOrani;
            ws.Cell(row, 9).Value = stok.SonKdvDahilBirimFiyat;
            ws.Cell(row, 10).Value = stok.StokDegeri;
            row++;
        }

        ws.Columns(4, 10).Style.NumberFormat.Format = "#,##0.00";
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
            $"stoklar_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    private async Task ListeyiYukleAsync(int firmaId)
    {
        var urunler = await _db.StokUrunler
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.Ad)
            .ToListAsync();

        BirimSecenekleri = urunler
            .Select(x => x.Birim)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        var urunIdleri = urunler.Select(x => x.Id).ToList();
        var hareketler = urunIdleri.Count == 0
            ? new List<StokHareket>()
            : await _db.StokHareketleri
                .AsNoTracking()
                .Where(x => x.FirmaId == firmaId && urunIdleri.Contains(x.StokUrunId))
                .OrderByDescending(x => x.Tarih)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

        var ozetler = urunler.Select(urun =>
        {
            var urunHareketleri = hareketler.Where(x => x.StokUrunId == urun.Id).ToList();
            var toplamGiris = urunHareketleri
                .Where(x => x.Tip == StokHareketTipi.Giris)
                .Sum(x => x.Miktar);
            var toplamCikis = urunHareketleri
                .Where(x => x.Tip == StokHareketTipi.Cikis)
                .Sum(x => x.Miktar);
            var mevcutStok = toplamGiris - toplamCikis;
            var sonGiris = urunHareketleri.FirstOrDefault(x => x.Tip == StokHareketTipi.Giris);
            var kdvDahilFiyat = sonGiris?.KdvDahilBirimFiyat ?? 0;

            return new StokOzet
            {
                Id = urun.Id,
                Ad = urun.Ad,
                Kod = urun.Kod,
                Birim = urun.Birim,
                MinStokSeviyesi = urun.MinStokSeviyesi,
                MevcutStok = mevcutStok,
                SonBirimFiyat = sonGiris?.BirimFiyat ?? 0,
                SonKdvOrani = sonGiris?.KdvOrani ?? 0,
                SonKdvDahilBirimFiyat = kdvDahilFiyat,
                StokDegeri = mevcutStok * kdvDahilFiyat,
                KritikMi = mevcutStok < urun.MinStokSeviyesi
            };
        }).ToList();

        ToplamUrunSayisi = ozetler.Count;
        KritikStokSayisi = ozetler.Count(x => x.KritikMi);
        ToplamStokDegeri = ozetler.Sum(x => x.StokDegeri);

        var bugun = DateTime.UtcNow.Date;
        var yarin = bugun.AddDays(1);
        BugunkuGirisMiktari = hareketler
            .Where(x => x.Tarih >= bugun && x.Tarih < yarin && x.Tip == StokHareketTipi.Giris)
            .Sum(x => x.Miktar);
        BugunkuCikisMiktari = hareketler
            .Where(x => x.Tarih >= bugun && x.Tarih < yarin && x.Tip == StokHareketTipi.Cikis)
            .Sum(x => x.Miktar);

        IEnumerable<StokOzet> filtreli = ozetler;

        if (!string.IsNullOrWhiteSpace(UrunAra))
        {
            var arama = UrunAra.Trim();
            filtreli = filtreli.Where(x =>
                x.Ad.Contains(arama, StringComparison.CurrentCultureIgnoreCase) ||
                x.Kod.Contains(arama, StringComparison.CurrentCultureIgnoreCase));
        }

        if (KritikFiltre)
            filtreli = filtreli.Where(x => x.KritikMi);

        if (!string.IsNullOrWhiteSpace(BirimFiltre))
            filtreli = filtreli.Where(x =>
                string.Equals(x.Birim, BirimFiltre.Trim(), StringComparison.CurrentCultureIgnoreCase));

        Liste = filtreli.OrderBy(x => x.Ad).ToList();
    }

    public class StokOzet
    {
        public int Id { get; set; }
        public string Ad { get; set; } = "";
        public string Kod { get; set; } = "";
        public string Birim { get; set; } = "";
        public decimal MinStokSeviyesi { get; set; }
        public decimal MevcutStok { get; set; }
        public decimal SonBirimFiyat { get; set; }
        public decimal SonKdvOrani { get; set; }
        public decimal SonKdvDahilBirimFiyat { get; set; }
        public decimal StokDegeri { get; set; }
        public bool KritikMi { get; set; }
    }
}
