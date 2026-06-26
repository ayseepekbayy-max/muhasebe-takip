using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public IndexModel(
        AppDbContext db,
        IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public decimal BugunGiris { get; set; }
    public decimal BugunCikis { get; set; }
    public decimal BuAyGiris { get; set; }
    public decimal BuAyCikis { get; set; }
    public decimal KasaBakiye { get; set; }
    public decimal BekleyenFaturaToplami { get; set; }
    public decimal OdenmisFaturaToplami { get; set; }

    public int CariSayisi { get; set; }
    public int MusteriSayisi { get; set; }
    public int CalisanSayisi { get; set; }
    public int KritikStokSayisi { get; set; }

    public List<KasaHareket> SonHareketler { get; set; } = new();
    public List<Models.IslemGecmisi> SonIslemler { get; set; } = new();
    public List<BildirimSatiri> Bildirimler { get; set; } = new();

    public string? SayfaHata { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await VerileriYukleAsync(firmaId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var hareket = await _db.KasaHareketleri
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.FirmaId == firmaId.Value);

        if (hareket == null)
        {
            TempData["Hata"] = "Kasa hareketi bulunamadı.";
            return RedirectToPage();
        }

        await _islemGecmisi.KaydetAsync(
            "Kasa",
            "Silme",
            $"Kasa hareketi ana sayfadan silindi (ID: {hareket.Id}).",
            eskiDeger: new
            {
                hareket.Id,
                hareket.Tarih,
                Tip = hareket.Tip.ToString(),
                hareket.Tutar,
                hareket.Aciklama,
                hareket.CariKartId,
                hareket.FaturaId
            });
        _db.KasaHareketleri.Remove(hareket);
        await _db.SaveChangesAsync();

        TempData["Basari"] = "Kasa hareketi silindi.";
        return RedirectToPage();
    }


    private async Task BildirimleriYukleAsync(int firmaId, DateTime bugun)
    {
        var yediGunSonra = bugun.AddDays(7);

        var gecikenFaturalar = await _db.Faturalar
            .AsNoTracking()
            .Include(x => x.CariKart)
            .Where(x => x.FirmaId == firmaId && x.Durum != FaturaDurumu.Iptal && x.VadeTarihi != null && x.VadeTarihi < bugun && x.GenelToplam > x.OdenenToplam)
            .OrderBy(x => x.VadeTarihi)
            .Take(5)
            .ToListAsync();

        foreach (var fatura in gecikenFaturalar)
        {
            Bildirimler.Add(new BildirimSatiri
            {
                Tur = "Geciken Fatura",
                Baslik = fatura.FaturaNo,
                Aciklama = $"{fatura.CariKart?.Unvan ?? "Cari yok"} - Kalan {fatura.KalanTutar:N2}",
                Url = $"/Faturalar/Detay/{fatura.Id}",
                Kritik = true
            });
        }

        var yaklasanFaturalar = await _db.Faturalar
            .AsNoTracking()
            .Include(x => x.CariKart)
            .Where(x => x.FirmaId == firmaId && x.Durum != FaturaDurumu.Iptal && x.VadeTarihi != null && x.VadeTarihi >= bugun && x.VadeTarihi <= yediGunSonra && x.GenelToplam > x.OdenenToplam)
            .OrderBy(x => x.VadeTarihi)
            .Take(5)
            .ToListAsync();

        foreach (var fatura in yaklasanFaturalar)
        {
            Bildirimler.Add(new BildirimSatiri
            {
                Tur = "Yaklaşan Vade",
                Baslik = fatura.FaturaNo,
                Aciklama = $"{fatura.VadeTarihi:dd.MM.yyyy} - {fatura.CariKart?.Unvan ?? "Cari yok"}",
                Url = $"/Faturalar/Detay/{fatura.Id}"
            });
        }

        var cekler = await _db.Cekler
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.Tarih >= bugun && x.Tarih <= yediGunSonra)
            .OrderBy(x => x.Tarih)
            .Take(5)
            .ToListAsync();

        foreach (var cek in cekler)
        {
            Bildirimler.Add(new BildirimSatiri
            {
                Tur = cek.Tip == CekTipi.Alinacak ? "Alınacak Çek" : "Ödenecek Çek",
                Baslik = string.IsNullOrWhiteSpace(cek.No) ? "Çek" : cek.No,
                Aciklama = $"{cek.Tarih:dd.MM.yyyy} - {cek.Tutar:N2}",
                Url = "/Cekler"
            });
        }

        var kritikStoklar = await _db.StokUrunler
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId)
            .Select(x => new
            {
                x.Id,
                x.Ad,
                x.MinStokSeviyesi,
                Giris = _db.StokHareketleri.Where(h => h.FirmaId == firmaId && h.StokUrunId == x.Id && h.Tip == StokHareketTipi.Giris).Sum(h => (decimal?)h.Miktar) ?? 0,
                Cikis = _db.StokHareketleri.Where(h => h.FirmaId == firmaId && h.StokUrunId == x.Id && h.Tip == StokHareketTipi.Cikis).Sum(h => (decimal?)h.Miktar) ?? 0
            })
            .Where(x => x.Giris - x.Cikis < x.MinStokSeviyesi)
            .Take(5)
            .ToListAsync();

        foreach (var stok in kritikStoklar)
        {
            Bildirimler.Add(new BildirimSatiri
            {
                Tur = "Stok Uyarısı",
                Baslik = stok.Ad,
                Aciklama = $"Stok eksiye düştü: {(stok.Giris - stok.Cikis):N2}",
                Url = $"/Stoklar/Detay/{stok.Id}",
                Kritik = true
            });
        }
    }
    private async Task VerileriYukleAsync(int firmaId)
    {
        try
        {
            var bugun = DateTime.UtcNow.Date;
            var ayBaslangic = new DateTime(bugun.Year, bugun.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var sonrakiAy = ayBaslangic.AddMonths(1);
            var yarin = bugun.AddDays(1);

            var kasaOzeti = await _db.KasaHareketleri
                .AsNoTracking()
                .Where(x => x.FirmaId == firmaId)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    BugunGiris = g.Where(x => x.Tarih >= bugun && x.Tarih < yarin && x.Tip == HareketTipi.Giris)
                        .Sum(x => (decimal?)x.Tutar) ?? 0,
                    BugunCikis = g.Where(x => x.Tarih >= bugun && x.Tarih < yarin && x.Tip == HareketTipi.Cikis)
                        .Sum(x => (decimal?)x.Tutar) ?? 0,
                    BuAyGiris = g.Where(x => x.Tarih >= ayBaslangic && x.Tarih < sonrakiAy && x.Tip == HareketTipi.Giris)
                        .Sum(x => (decimal?)x.Tutar) ?? 0,
                    BuAyCikis = g.Where(x => x.Tarih >= ayBaslangic && x.Tarih < sonrakiAy && x.Tip == HareketTipi.Cikis)
                        .Sum(x => (decimal?)x.Tutar) ?? 0,
                    ToplamGiris = g.Where(x => x.Tip == HareketTipi.Giris).Sum(x => (decimal?)x.Tutar) ?? 0,
                    ToplamCikis = g.Where(x => x.Tip == HareketTipi.Cikis).Sum(x => (decimal?)x.Tutar) ?? 0
                })
                .FirstOrDefaultAsync();

            BugunGiris = kasaOzeti?.BugunGiris ?? 0;
            BugunCikis = kasaOzeti?.BugunCikis ?? 0;
            BuAyGiris = kasaOzeti?.BuAyGiris ?? 0;
            BuAyCikis = kasaOzeti?.BuAyCikis ?? 0;
            KasaBakiye = (kasaOzeti?.ToplamGiris ?? 0) - (kasaOzeti?.ToplamCikis ?? 0);

            var faturaOzeti = await _db.Faturalar
                .AsNoTracking()
                .Where(x => x.FirmaId == firmaId && x.Durum != FaturaDurumu.Iptal)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Bekleyen = g.Where(x => x.Durum == FaturaDurumu.Bekliyor || x.Durum == FaturaDurumu.KismenOdendi)
                        .Sum(x => (decimal?)(x.GenelToplam - x.OdenenToplam)) ?? 0,
                    Odenmis = g.Where(x => x.Durum == FaturaDurumu.Odendi)
                        .Sum(x => (decimal?)x.GenelToplam) ?? 0
                })
                .FirstOrDefaultAsync();

            BekleyenFaturaToplami = faturaOzeti?.Bekleyen ?? 0;
            OdenmisFaturaToplami = faturaOzeti?.Odenmis ?? 0;

            CariSayisi = await _db.CariKartlar
                .AsNoTracking()
                .CountAsync(x => x.FirmaId == firmaId);

            MusteriSayisi = await _db.Musteriler
                .AsNoTracking()
                .CountAsync(x => x.FirmaId == firmaId);

            CalisanSayisi = await _db.Calisanlar
                .AsNoTracking()
                .CountAsync(x => x.FirmaId == firmaId && x.AktifMi);

            KritikStokSayisi = await _db.StokUrunler
                .AsNoTracking()
                .Where(x => x.FirmaId == firmaId)
                .CountAsync(urun =>
                    (_db.StokHareketleri
                        .Where(h => h.FirmaId == firmaId && h.StokUrunId == urun.Id && h.Tip == StokHareketTipi.Giris)
                        .Sum(h => (decimal?)h.Miktar) ?? 0) -
                    (_db.StokHareketleri
                        .Where(h => h.FirmaId == firmaId && h.StokUrunId == urun.Id && h.Tip == StokHareketTipi.Cikis)
                        .Sum(h => (decimal?)h.Miktar) ?? 0) < urun.MinStokSeviyesi);

            await BildirimleriYukleAsync(firmaId, bugun);

            SonHareketler = await _db.KasaHareketleri
                .AsNoTracking()
                .Include(x => x.CariKart)
                .Where(x => x.FirmaId == firmaId)
                .OrderByDescending(x => x.Tarih)
                .ThenByDescending(x => x.Id)
                .Take(10)
                .ToListAsync();

            SonIslemler = await _db.IslemGecmisleri
                .AsNoTracking()
                .Where(x => x.FirmaId == firmaId)
                .OrderByDescending(x => x.Tarih)
                .ThenByDescending(x => x.Id)
                .Take(10)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            BugunGiris = 0;
            BugunCikis = 0;
            BuAyGiris = 0;
            BuAyCikis = 0;
            KasaBakiye = 0;
            BekleyenFaturaToplami = 0;
            OdenmisFaturaToplami = 0;
            CariSayisi = 0;
            MusteriSayisi = 0;
            CalisanSayisi = 0;
            KritikStokSayisi = 0;
            SonHareketler = new List<KasaHareket>();
            SonIslemler = new List<Models.IslemGecmisi>();

            SayfaHata = ex.Message;
        }
    }
}
public class BildirimSatiri
{
    public string Tur { get; set; } = "";
    public string Baslik { get; set; } = "";
    public string Aciklama { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Kritik { get; set; }
}
