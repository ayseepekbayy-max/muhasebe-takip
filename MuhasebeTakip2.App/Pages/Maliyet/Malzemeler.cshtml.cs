using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using System.Globalization;

namespace MuhasebeTakip2.App.Pages.Maliyet;

public class MalzemelerModel : PageModel
{
    private readonly AppDbContext _db;

    public MalzemelerModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public int? SeciliStokUrunId { get; set; }

    [BindProperty]
    public decimal BirParcaKullanimMiktari { get; set; }

    [BindProperty]
    public decimal BirParcaMalzemeMaliyeti { get; set; }

    [BindProperty]
    public int Adet { get; set; }

    public List<StokUrun> StokUrunleri { get; set; } = new();

    public bool Hesaplandi { get; set; }

    public decimal PlakaBirParcaMaliyeti { get; set; }
    public decimal BantBirParcaMaliyeti { get; set; }

    public decimal ToplamKullanilacakMiktar { get; set; }
    public decimal ToplamMalzemeMaliyeti { get; set; }

    public decimal ToplamBirParcaMaliyet { get; set; }
    public decimal ToplamGenelMaliyet { get; set; }

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleStoklariAsync(firmaId.Value);
        SessiondanMaliyetleriYukle(firmaId.Value);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleStoklariAsync(firmaId.Value);
        SessiondanMaliyetleriYukle(firmaId.Value);

        if (SeciliStokUrunId == null || SeciliStokUrunId <= 0)
        {
            Hata = "Lütfen stoktan bir ürün seçin.";
            return Page();
        }

        if (BirParcaKullanimMiktari <= 0 || BirParcaMalzemeMaliyeti <= 0 || Adet <= 0)
        {
            Hata = "Tüm alanlara 0'dan büyük değer girin.";
            return Page();
        }

        ToplamKullanilacakMiktar = Math.Round(BirParcaKullanimMiktari * Adet, 2);
        ToplamMalzemeMaliyeti = Math.Round(BirParcaMalzemeMaliyeti * Adet, 2);

        ToplamBirParcaMaliyet = Math.Round(
            PlakaBirParcaMaliyeti + BantBirParcaMaliyeti + BirParcaMalzemeMaliyeti, 2);

        ToplamGenelMaliyet = Math.Round(ToplamBirParcaMaliyet * Adet, 2);

        HttpContext.Session.SetInt32($"Maliyet_{firmaId.Value}_Adet", Adet);

        HttpContext.Session.SetString(
            $"Maliyet_{firmaId.Value}_MalzemeBirParcaMaliyeti",
            BirParcaMalzemeMaliyeti.ToString(CultureInfo.InvariantCulture));

        HttpContext.Session.SetString(
            $"Maliyet_{firmaId.Value}_MalzemeToplamMaliyeti",
            ToplamMalzemeMaliyeti.ToString(CultureInfo.InvariantCulture));

        HttpContext.Session.SetString(
            $"Maliyet_{firmaId.Value}_ToplamBirParcaMaliyet_Tum",
            ToplamBirParcaMaliyet.ToString(CultureInfo.InvariantCulture));

        HttpContext.Session.SetString(
            $"Maliyet_{firmaId.Value}_ToplamGenelMaliyet_Tum",
            ToplamGenelMaliyet.ToString(CultureInfo.InvariantCulture));

        Hesaplandi = true;
        return Page();
    }

    public async Task<IActionResult> OnPostStoktanDusAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleStoklariAsync(firmaId.Value);
        SessiondanMaliyetleriYukle(firmaId.Value);

        if (SeciliStokUrunId == null || SeciliStokUrunId <= 0)
        {
            Hata = "Lütfen stoktan bir ürün seçin.";
            return Page();
        }

        if (BirParcaKullanimMiktari <= 0 || Adet <= 0)
        {
            Hata = "Geçerli kullanım miktarı bulunamadı.";
            return Page();
        }

        var urun = await _db.StokUrunler
            .FirstOrDefaultAsync(x => x.Id == SeciliStokUrunId && x.FirmaId == firmaId.Value);

        if (urun == null)
        {
            Hata = "Seçilen stok ürünü bulunamadı.";
            return Page();
        }

        ToplamKullanilacakMiktar = Math.Round(BirParcaKullanimMiktari * Adet, 2);
        ToplamMalzemeMaliyeti = Math.Round(BirParcaMalzemeMaliyeti * Adet, 2);
        ToplamBirParcaMaliyet = Math.Round(
            PlakaBirParcaMaliyeti + BantBirParcaMaliyeti + BirParcaMalzemeMaliyeti, 2);
        ToplamGenelMaliyet = Math.Round(ToplamBirParcaMaliyet * Adet, 2);

        var stok = await _db.StokHareketleri
            .Where(x => x.StokUrunId == urun.Id && x.FirmaId == firmaId.Value)
            .GroupBy(x => 1)
            .Select(g => new
            {
                Giris = g.Where(x => x.Tip == StokHareketTipi.Giris).Sum(x => (decimal?)x.Miktar) ?? 0,
                Cikis = g.Where(x => x.Tip == StokHareketTipi.Cikis).Sum(x => (decimal?)x.Miktar) ?? 0
            })
            .FirstOrDefaultAsync();

        var mevcutStok = (stok?.Giris ?? 0) - (stok?.Cikis ?? 0);

        if (mevcutStok < ToplamKullanilacakMiktar)
        {
            Hata = $"Yetersiz stok! Mevcut: {mevcutStok}";
            Hesaplandi = true;
            return Page();
        }

        var hareket = new StokHareket
        {
            FirmaId = firmaId.Value,
            StokUrunId = urun.Id,
            Tarih = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc),
            Tip = StokHareketTipi.Cikis,
            Miktar = ToplamKullanilacakMiktar,
            Aciklama = "Malzemeler bölümünden otomatik stok düşümü"
        };

        _db.StokHareketleri.Add(hareket);
        await _db.SaveChangesAsync();

        HttpContext.Session.SetInt32($"Maliyet_{firmaId.Value}_Adet", Adet);

        Hesaplandi = true;
        Mesaj = "Stoktan düşüm başarıyla yapıldı.";
        return Page();
    }

    private async Task YukleStoklariAsync(int firmaId)
    {
        StokUrunleri = await _db.StokUrunler
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.Ad)
            .ToListAsync();
    }

    private void SessiondanMaliyetleriYukle(int firmaId)
    {
        var plaka = HttpContext.Session.GetString($"Maliyet_{firmaId}_PlakaBirParcaMaliyeti");
        if (!string.IsNullOrWhiteSpace(plaka))
        {
            if (decimal.TryParse(plaka, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedPlaka))
                PlakaBirParcaMaliyeti = parsedPlaka;
        }

        var bant = HttpContext.Session.GetString($"Maliyet_{firmaId}_BantBirParcaMaliyeti");
        if (!string.IsNullOrWhiteSpace(bant))
        {
            if (decimal.TryParse(bant, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedBant))
                BantBirParcaMaliyeti = parsedBant;
        }
    }
}