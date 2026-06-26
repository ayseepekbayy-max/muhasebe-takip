using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages;

public class RaporlarModel : PageModel
{
    private readonly AppDbContext _db;

    public RaporlarModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public DateTime? BaslangicTarihi { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? BitisTarihi { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Hizli { get; set; }

    public DateTime SeciliBaslangic { get; set; }
    public DateTime SeciliBitis { get; set; }
    public DateTime SeciliBitisHaric { get; set; }

    public decimal BugunkuGelir { get; set; }
    public decimal BugunkuGider { get; set; }
    public decimal BuAyGelir { get; set; }
    public decimal BuAyGider { get; set; }
    public decimal BuAyKarZarar => BuAyGelir - BuAyGider;
    public decimal ToplamStokDegeri { get; set; }
    public decimal BekleyenFaturaTutari { get; set; }
    public decimal PersonelGideri { get; set; }

    public decimal SeciliGelir { get; set; }
    public decimal SeciliGider { get; set; }
    public decimal SeciliNet => SeciliGelir - SeciliGider;

    public decimal ToplamMaasOdemesi { get; set; }
    public decimal ToplamAvans { get; set; }
    public decimal NetPersonelGideri => ToplamMaasOdemesi + ToplamAvans;

    public int BekleyenFaturaSayisi { get; set; }
    public int OdenmisFaturaSayisi { get; set; }
    public int IptalFaturaSayisi { get; set; }
    public decimal OdenmisFaturaTutari { get; set; }
    public decimal IptalFaturaTutari { get; set; }
    public decimal ToplamFaturaTutari { get; set; }

    public List<RaporSatiri> EnCokTahsilatYapilanlar { get; set; } = new();
    public List<RaporSatiri> EnCokOdemeYapilanlar { get; set; } = new();
    public List<StokHareketRaporSatiri> EnCokHareketGorenStoklar { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        TarihAraliginiHazirla();
        await RaporlariYukleAsync(firmaId.Value);

        return Page();
    }

    public async Task<IActionResult> OnGetDisaAktarAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        TarihAraliginiHazirla();
        await RaporlariYukleAsync(firmaId.Value);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Rapor Özeti");

        ws.Cell(1, 1).Value = "Rapor";
        ws.Cell(1, 2).Value = "Değer";

        var satirlar = new (string Baslik, object Deger)[]
        {
            ("Başlangıç", SeciliBaslangic),
            ("Bitiş", SeciliBitis),
            ("Bugünkü Gelir", BugunkuGelir),
            ("Bugünkü Gider", BugunkuGider),
            ("Bu Ay Gelir", BuAyGelir),
            ("Bu Ay Gider", BuAyGider),
            ("Bu Ay Kâr/Zarar", BuAyKarZarar),
            ("Seçilen Aralık Gelir", SeciliGelir),
            ("Seçilen Aralık Gider", SeciliGider),
            ("Seçilen Aralık Net", SeciliNet),
            ("Toplam Stok Değeri", ToplamStokDegeri),
            ("Bekleyen Fatura Tutarı", BekleyenFaturaTutari),
            ("Personel Gideri", PersonelGideri),
            ("Toplam Maaş Ödemesi", ToplamMaasOdemesi),
            ("Toplam Avans", ToplamAvans),
            ("Net Personel Gideri", NetPersonelGideri),
            ("Bekleyen Faturalar", BekleyenFaturaSayisi),
            ("Ödenmiş Faturalar", OdenmisFaturaSayisi),
            ("İptal Faturalar", IptalFaturaSayisi),
            ("Toplam Fatura Tutarı", ToplamFaturaTutari)
        };

        for (var i = 0; i < satirlar.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = satirlar[i].Baslik;
            if (satirlar[i].Deger is DateTime tarih)
            {
                ws.Cell(i + 2, 2).Value = tarih;
                ws.Cell(i + 2, 2).Style.DateFormat.Format = "dd.MM.yyyy";
            }
            else if (satirlar[i].Deger is int sayi)
            {
                ws.Cell(i + 2, 2).Value = sayi;
            }
            else
            {
                ws.Cell(i + 2, 2).Value = Convert.ToDecimal(satirlar[i].Deger);
                ws.Cell(i + 2, 2).Style.NumberFormat.Format = "#,##0.00 ₺";
            }
        }

        var sonSatir = satirlar.Length + 1;
        var header = ws.Range(1, 1, 1, 2);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.LightGray;
        header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Range(1, 1, sonSatir, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range(1, 1, sonSatir, 2).Style.Border.InsideBorder = XLBorderStyleValues.Thin;

        ExcelListeYaz(workbook, "Top Tahsilat", EnCokTahsilatYapilanlar);
        ExcelListeYaz(workbook, "Top Ödeme", EnCokOdemeYapilanlar);
        ExcelStokListeYaz(workbook, "Stok Hareketleri", EnCokHareketGorenStoklar);

        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"raporlar_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx");
    }

    public async Task<IActionResult> OnPostDisaAktarAsync() => await OnGetDisaAktarAsync();

    private void TarihAraliginiHazirla()
    {
        var bugun = DateTime.UtcNow.Date;
        var buAyBaslangic = new DateTime(bugun.Year, bugun.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        if (string.Equals(Hizli, "bugun", StringComparison.OrdinalIgnoreCase))
        {
            SeciliBaslangic = bugun;
            SeciliBitis = bugun;
        }
        else if (string.Equals(Hizli, "gecen-ay", StringComparison.OrdinalIgnoreCase))
        {
            SeciliBaslangic = buAyBaslangic.AddMonths(-1);
            SeciliBitis = buAyBaslangic.AddDays(-1);
        }
        else if (string.Equals(Hizli, "bu-ay", StringComparison.OrdinalIgnoreCase))
        {
            SeciliBaslangic = buAyBaslangic;
            SeciliBitis = bugun;
        }
        else
        {
            SeciliBaslangic = TarihiUtcYap(BaslangicTarihi ?? buAyBaslangic);
            SeciliBitis = TarihiUtcYap(BitisTarihi ?? bugun);
        }

        if (SeciliBitis < SeciliBaslangic)
            SeciliBitis = SeciliBaslangic;

        SeciliBitisHaric = SeciliBitis.AddDays(1);
        BaslangicTarihi = SeciliBaslangic;
        BitisTarihi = SeciliBitis;
    }

    private async Task RaporlariYukleAsync(int firmaId)
    {
        var bugun = DateTime.UtcNow.Date;
        var yarin = bugun.AddDays(1);
        var buAyBaslangic = new DateTime(bugun.Year, bugun.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sonrakiAy = buAyBaslangic.AddMonths(1);

        var bugunOzeti = await KasaOzetiAsync(firmaId, bugun, yarin);
        BugunkuGelir = bugunOzeti.Gelir;
        BugunkuGider = bugunOzeti.Gider;

        var ayOzeti = await KasaOzetiAsync(firmaId, buAyBaslangic, sonrakiAy);
        BuAyGelir = ayOzeti.Gelir;
        BuAyGider = ayOzeti.Gider;

        var seciliOzet = await KasaOzetiAsync(firmaId, SeciliBaslangic, SeciliBitisHaric);
        SeciliGelir = seciliOzet.Gelir;
        SeciliGider = seciliOzet.Gider;

        var personelOzeti = await _db.CalisanAvanslari
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.Tarih >= SeciliBaslangic && x.Tarih < SeciliBitisHaric)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Maas = g.Where(x => x.Tip == CalisanHareketTipi.MaasOdeme || x.Tip == CalisanHareketTipi.Diger)
                    .Sum(x => (decimal?)x.Tutar) ?? 0,
                Avans = g.Where(x => x.Tip == CalisanHareketTipi.Avans)
                    .Sum(x => (decimal?)x.Tutar) ?? 0
            })
            .FirstOrDefaultAsync();

        ToplamMaasOdemesi = personelOzeti?.Maas ?? 0;
        ToplamAvans = personelOzeti?.Avans ?? 0;
        PersonelGideri = ToplamMaasOdemesi + ToplamAvans;

        await FaturaOzetiniYukleAsync(firmaId);
        await StokOzetiniYukleAsync(firmaId);

        EnCokTahsilatYapilanlar = await TopCariHareketleriAsync(firmaId, HareketTipi.Giris);
        EnCokOdemeYapilanlar = await TopCariHareketleriAsync(firmaId, HareketTipi.Cikis);
        EnCokHareketGorenStoklar = await TopStokHareketleriAsync(firmaId);
    }

    private async Task<(decimal Gelir, decimal Gider)> KasaOzetiAsync(int firmaId, DateTime baslangic, DateTime bitisHaric)
    {
        var ozet = await _db.KasaHareketleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.Tarih >= baslangic && x.Tarih < bitisHaric)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Gelir = g.Where(x => x.Tip == HareketTipi.Giris).Sum(x => (decimal?)x.Tutar) ?? 0,
                Gider = g.Where(x => x.Tip == HareketTipi.Cikis).Sum(x => (decimal?)x.Tutar) ?? 0
            })
            .FirstOrDefaultAsync();

        return (ozet?.Gelir ?? 0, ozet?.Gider ?? 0);
    }

    private async Task FaturaOzetiniYukleAsync(int firmaId)
    {
        var ozet = await _db.Faturalar
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.Tarih >= SeciliBaslangic && x.Tarih < SeciliBitisHaric)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                BekleyenSayisi = g.Count(x => x.Durum == FaturaDurumu.Bekliyor || x.Durum == FaturaDurumu.KismenOdendi),
                BekleyenTutari = g.Where(x => x.Durum == FaturaDurumu.Bekliyor || x.Durum == FaturaDurumu.KismenOdendi)
                    .Sum(x => (decimal?)(x.GenelToplam - x.OdenenToplam)) ?? 0,
                OdenmisSayisi = g.Count(x => x.Durum == FaturaDurumu.Odendi),
                OdenmisTutari = g.Where(x => x.Durum == FaturaDurumu.Odendi)
                    .Sum(x => (decimal?)x.GenelToplam) ?? 0,
                IptalSayisi = g.Count(x => x.Durum == FaturaDurumu.Iptal),
                IptalTutari = g.Where(x => x.Durum == FaturaDurumu.Iptal)
                    .Sum(x => (decimal?)x.GenelToplam) ?? 0,
                Toplam = g.Sum(x => (decimal?)x.GenelToplam) ?? 0
            })
            .FirstOrDefaultAsync();

        BekleyenFaturaSayisi = ozet?.BekleyenSayisi ?? 0;
        BekleyenFaturaTutari = ozet?.BekleyenTutari ?? 0;
        OdenmisFaturaSayisi = ozet?.OdenmisSayisi ?? 0;
        OdenmisFaturaTutari = ozet?.OdenmisTutari ?? 0;
        IptalFaturaSayisi = ozet?.IptalSayisi ?? 0;
        IptalFaturaTutari = ozet?.IptalTutari ?? 0;
        ToplamFaturaTutari = ozet?.Toplam ?? 0;
    }

    private async Task StokOzetiniYukleAsync(int firmaId)
    {
        var stokDegerleri = await _db.StokUrunler
            .AsNoTracking()
            .Where(urun => urun.FirmaId == firmaId)
            .Select(urun => new
            {
                MevcutStok = (_db.StokHareketleri
                    .Where(x => x.FirmaId == firmaId && x.StokUrunId == urun.Id && x.Tip == StokHareketTipi.Giris)
                    .Sum(x => (decimal?)x.Miktar) ?? 0) -
                    (_db.StokHareketleri
                        .Where(x => x.FirmaId == firmaId && x.StokUrunId == urun.Id && x.Tip == StokHareketTipi.Cikis)
                        .Sum(x => (decimal?)x.Miktar) ?? 0),
                SonFiyat = _db.StokHareketleri
                    .Where(x => x.FirmaId == firmaId && x.StokUrunId == urun.Id && x.Tip == StokHareketTipi.Giris)
                    .OrderByDescending(x => x.Tarih)
                    .ThenByDescending(x => x.Id)
                    .Select(x => (decimal?)(x.Miktar > 0
                        ? ((x.Miktar * x.BirimFiyat) + ((x.Miktar * x.BirimFiyat) * x.KdvOrani / 100)) / x.Miktar
                        : 0))
                    .FirstOrDefault() ?? 0
            })
            .ToListAsync();

        ToplamStokDegeri = stokDegerleri.Sum(x => x.MevcutStok * x.SonFiyat);
    }

    private async Task<List<RaporSatiri>> TopCariHareketleriAsync(int firmaId, HareketTipi tip)
    {
        return await _db.KasaHareketleri
            .AsNoTracking()
            .Where(x =>
                x.FirmaId == firmaId &&
                x.CariKartId != null &&
                x.Tarih >= SeciliBaslangic &&
                x.Tarih < SeciliBitisHaric &&
                x.Tip == tip)
            .GroupBy(x => new { x.CariKartId, x.CariKart!.Unvan })
            .Select(g => new RaporSatiri
            {
                Id = g.Key.CariKartId ?? 0,
                Ad = string.IsNullOrWhiteSpace(g.Key.Unvan) ? "Cari belirtilmemiş" : g.Key.Unvan,
                Tutar = g.Sum(x => x.Tutar),
                Adet = g.Count()
            })
            .OrderByDescending(x => x.Tutar)
            .Take(5)
            .ToListAsync();
    }

    private async Task<List<StokHareketRaporSatiri>> TopStokHareketleriAsync(int firmaId)
    {
        return await _db.StokHareketleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.Tarih >= SeciliBaslangic && x.Tarih < SeciliBitisHaric)
            .GroupBy(x => new { x.StokUrunId, x.StokUrun!.Ad, x.StokUrun.Kod })
            .Select(g => new StokHareketRaporSatiri
            {
                Id = g.Key.StokUrunId,
                Ad = string.IsNullOrWhiteSpace(g.Key.Ad) ? "Ürün belirtilmemiş" : g.Key.Ad,
                Kod = g.Key.Kod,
                Giris = g.Where(x => x.Tip == StokHareketTipi.Giris).Sum(x => (decimal?)x.Miktar) ?? 0,
                Cikis = g.Where(x => x.Tip == StokHareketTipi.Cikis).Sum(x => (decimal?)x.Miktar) ?? 0,
                HareketSayisi = g.Count()
            })
            .OrderByDescending(x => x.HareketSayisi)
            .ThenByDescending(x => x.Giris + x.Cikis)
            .Take(5)
            .ToListAsync();
    }

    private static DateTime TarihiUtcYap(DateTime tarih)
    {
        var sadeceTarih = tarih.Date;
        return tarih.Kind switch
        {
            DateTimeKind.Utc => sadeceTarih,
            DateTimeKind.Local => tarih.ToUniversalTime().Date,
            _ => DateTime.SpecifyKind(sadeceTarih, DateTimeKind.Utc)
        };
    }

    private static void ExcelListeYaz(XLWorkbook workbook, string sayfaAdi, List<RaporSatiri> liste)
    {
        var ws = workbook.Worksheets.Add(sayfaAdi);
        ws.Cell(1, 1).Value = "Ad";
        ws.Cell(1, 2).Value = "Adet";
        ws.Cell(1, 3).Value = "Tutar";

        for (var i = 0; i < liste.Count; i++)
        {
            ws.Cell(i + 2, 1).Value = liste[i].Ad;
            ws.Cell(i + 2, 2).Value = liste[i].Adet;
            ws.Cell(i + 2, 3).Value = liste[i].Tutar;
            ws.Cell(i + 2, 3).Style.NumberFormat.Format = "#,##0.00 ₺";
        }

        ws.Range(1, 1, Math.Max(1, liste.Count + 1), 3).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range(1, 1, Math.Max(1, liste.Count + 1), 3).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
    }

    private static void ExcelStokListeYaz(XLWorkbook workbook, string sayfaAdi, List<StokHareketRaporSatiri> liste)
    {
        var ws = workbook.Worksheets.Add(sayfaAdi);
        ws.Cell(1, 1).Value = "Ürün";
        ws.Cell(1, 2).Value = "Kod";
        ws.Cell(1, 3).Value = "Giriş";
        ws.Cell(1, 4).Value = "Çıkış";
        ws.Cell(1, 5).Value = "Hareket Sayısı";

        for (var i = 0; i < liste.Count; i++)
        {
            ws.Cell(i + 2, 1).Value = liste[i].Ad;
            ws.Cell(i + 2, 2).Value = liste[i].Kod ?? "";
            ws.Cell(i + 2, 3).Value = liste[i].Giris;
            ws.Cell(i + 2, 4).Value = liste[i].Cikis;
            ws.Cell(i + 2, 5).Value = liste[i].HareketSayisi;
        }

        ws.Range(1, 1, Math.Max(1, liste.Count + 1), 5).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range(1, 1, Math.Max(1, liste.Count + 1), 5).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
    }

    public class RaporSatiri
    {
        public int Id { get; set; }
        public string Ad { get; set; } = "";
        public decimal Tutar { get; set; }
        public int Adet { get; set; }
    }

    public class StokHareketRaporSatiri
    {
        public int Id { get; set; }
        public string Ad { get; set; } = "";
        public string? Kod { get; set; }
        public decimal Giris { get; set; }
        public decimal Cikis { get; set; }
        public int HareketSayisi { get; set; }
    }
}
