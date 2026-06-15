using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public decimal BugunGiris { get; set; }
    public decimal BugunCikis { get; set; }
    public decimal BuAyGiris { get; set; }
    public decimal BuAyCikis { get; set; }
    public decimal KasaBakiye { get; set; }

    public int CariSayisi { get; set; }
    public int CalisanSayisi { get; set; }

    public List<KasaHareket> SonHareketler { get; set; } = new();
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

        _db.KasaHareketleri.Remove(hareket);
        await _db.SaveChangesAsync();

        TempData["Basari"] = "Kasa hareketi silindi.";
        return RedirectToPage();
    }


    private async Task BildirimleriYukleAsync(int firmaId, DateTime bugun)
    {
        var yediGunSonra = bugun.AddDays(7);

        var gecikenFaturalar = await _db.Faturalar
            .Include(x => x.CariKart)
            .Where(x => x.FirmaId == firmaId && x.VadeTarihi != null && x.VadeTarihi < bugun && x.GenelToplam > x.OdenenToplam)
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
            .Include(x => x.CariKart)
            .Where(x => x.FirmaId == firmaId && x.VadeTarihi != null && x.VadeTarihi >= bugun && x.VadeTarihi <= yediGunSonra && x.GenelToplam > x.OdenenToplam)
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

        var stoklar = await _db.StokUrunler
            .Where(x => x.FirmaId == firmaId)
            .Select(x => new
            {
                x.Id,
                x.Ad,
                Giris = _db.StokHareketleri.Where(h => h.FirmaId == firmaId && h.StokUrunId == x.Id && h.Tip == StokHareketTipi.Giris).Sum(h => (decimal?)h.Miktar) ?? 0,
                Cikis = _db.StokHareketleri.Where(h => h.FirmaId == firmaId && h.StokUrunId == x.Id && h.Tip == StokHareketTipi.Cikis).Sum(h => (decimal?)h.Miktar) ?? 0
            })
            .ToListAsync();

        foreach (var stok in stoklar.Where(x => x.Giris - x.Cikis < 0).Take(5))
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
            var yarin = bugun.AddDays(1);

            BugunGiris = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= bugun &&
                    x.Tarih < yarin &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            BugunCikis = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= bugun &&
                    x.Tarih < yarin &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            BuAyGiris = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            BuAyCikis = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tarih >= ayBaslangic &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var toplamGiris = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tip == HareketTipi.Giris)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            var toplamCikis = await _db.KasaHareketleri
                .Where(x =>
                    x.FirmaId == firmaId &&
                    x.Tip == HareketTipi.Cikis)
                .SumAsync(x => (decimal?)x.Tutar) ?? 0;

            KasaBakiye = toplamGiris - toplamCikis;

            CariSayisi = await _db.CariKartlar
                .CountAsync(x => x.FirmaId == firmaId);

            CalisanSayisi = await _db.Calisanlar
                .CountAsync(x => x.FirmaId == firmaId);

            await BildirimleriYukleAsync(firmaId, bugun);

            SonHareketler = await _db.KasaHareketleri
                .Include(x => x.CariKart)
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
            CariSayisi = 0;
            CalisanSayisi = 0;
            SonHareketler = new List<KasaHareket>();

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