using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Maliyet;

public class UretimModel : PageModel
{
    private readonly AppDbContext _db;

    public UretimModel(AppDbContext db)
    {
        _db = db;
    }

    [BindProperty]
    public string UretimAdi { get; set; } = "";

    [BindProperty]
    public decimal UretimAdedi { get; set; }

    [BindProperty]
    public decimal PlakaEnCm { get; set; }

    [BindProperty]
    public decimal PlakaBoyCm { get; set; }

    [BindProperty]
    public decimal PlakaFiyati { get; set; }

    [BindProperty]
    public decimal ParcaEnCm { get; set; }

    [BindProperty]
    public decimal ParcaBoyCm { get; set; }

    [BindProperty]
    public decimal ParcaAdedi { get; set; }

    [BindProperty]
    public decimal BantParcaEnCm { get; set; }

    [BindProperty]
    public decimal BantParcaBoyCm { get; set; }

    [BindProperty]
    public decimal BantParcaAdedi { get; set; }

    [BindProperty]
    public decimal BantMetreFiyati { get; set; }

    [BindProperty]
    public bool BantUstAlt { get; set; } = true;

    [BindProperty]
    public bool BantSagSol { get; set; } = true;

    [BindProperty]
    public bool StoktanDus { get; set; }

    [BindProperty]
    public List<MalzemeSatiri> Malzemeler { get; set; } = new();

    public List<StokUrun> StokUrunleri { get; set; } = new();

    public decimal PlakaMaliyeti { get; set; }
    public decimal BantlamaMaliyeti { get; set; }
    public decimal MalzemeMaliyeti { get; set; }
    public decimal ToplamMaliyet { get; set; }
    public decimal BirimMaliyet { get; set; }

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";
    public bool HesaplandiMi { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        FormuHazirla();
        await StoklariYukleAsync(firmaId.Value);

        return Page();
    }

    public async Task<IActionResult> OnPostHesaplaAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await StoklariYukleAsync(firmaId.Value);
        MalzemeSatirlariniTamamla();

        Hesapla();

        HesaplandiMi = true;
        return Page();
    }

    public async Task<IActionResult> OnPostStokDusAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await StoklariYukleAsync(firmaId.Value);
        MalzemeSatirlariniTamamla();

        Hesapla();
        HesaplandiMi = true;

        if (!StoktanDus)
        {
            Hata = "Stoktan düşmek için 'Malzemeleri stoktan düş' seçeneğini işaretleyin.";
            return Page();
        }

        var kullanilacaklar = Malzemeler
            .Where(x => x.StokUrunId > 0 && x.ToplamKullanimMiktari > 0)
            .ToList();

        if (!kullanilacaklar.Any())
        {
            Hata = "Stoktan düşülecek malzeme bulunamadı.";
            return Page();
        }

        foreach (var satir in kullanilacaklar)
        {
            var urun = await _db.StokUrunler
                .FirstOrDefaultAsync(x =>
                    x.Id == satir.StokUrunId &&
                    x.FirmaId == firmaId.Value);

            if (urun == null)
                continue;

            var mevcut = await StokMiktariGetirAsync(firmaId.Value, urun.Id);

            if (mevcut < satir.ToplamKullanimMiktari)
            {
                Hata = $"{urun.Ad} için yeterli stok yok. Mevcut: {mevcut:N2}, gerekli: {satir.ToplamKullanimMiktari:N2}";
                return Page();
            }

            _db.StokHareketleri.Add(new StokHareket
            {
                FirmaId = firmaId.Value,
                StokUrunId = urun.Id,
                Tarih = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Utc),
                Tip = StokHareketTipi.Cikis,
                Miktar = satir.ToplamKullanimMiktari,
                Aciklama = $"Üretim maliyeti kullanımı - {UretimAdi}"
            });
        }

        await _db.SaveChangesAsync();

        Mesaj = "Malzemeler stoktan düşüldü.";
        return Page();
    }

    private void Hesapla()
    {
        UretimAdi = (UretimAdi ?? "").Trim();

        if (UretimAdedi <= 0)
            UretimAdedi = 1;

        PlakaMaliyeti = HesaplaPlakaMaliyeti();
        BantlamaMaliyeti = HesaplaBantlamaMaliyeti();
        MalzemeMaliyeti = HesaplaMalzemeMaliyeti();

        ToplamMaliyet = PlakaMaliyeti + BantlamaMaliyeti + MalzemeMaliyeti;
        BirimMaliyet = UretimAdedi > 0 ? ToplamMaliyet / UretimAdedi : 0;
    }

    private decimal HesaplaPlakaMaliyeti()
    {
        if (PlakaEnCm <= 0 || PlakaBoyCm <= 0 || PlakaFiyati <= 0 || ParcaEnCm <= 0 || ParcaBoyCm <= 0)
            return 0;

        var adet = ParcaAdedi > 0 ? ParcaAdedi : UretimAdedi;

        var plakaAlanM2 = (PlakaEnCm * PlakaBoyCm) / 10000m;
        var parcaAlanM2 = (ParcaEnCm * ParcaBoyCm) / 10000m;
        var toplamParcaAlanM2 = parcaAlanM2 * adet;

        if (plakaAlanM2 <= 0)
            return 0;

        return toplamParcaAlanM2 / plakaAlanM2 * PlakaFiyati;
    }

    private decimal HesaplaBantlamaMaliyeti()
    {
        if (BantParcaEnCm <= 0 || BantParcaBoyCm <= 0 || BantMetreFiyati <= 0)
            return 0;

        var adet = BantParcaAdedi > 0 ? BantParcaAdedi : UretimAdedi;

        decimal toplamCm = 0;

        if (BantUstAlt)
            toplamCm += BantParcaEnCm * 2;

        if (BantSagSol)
            toplamCm += BantParcaBoyCm * 2;

        if (!BantUstAlt && !BantSagSol)
            toplamCm = (BantParcaEnCm + BantParcaBoyCm) * 2;

        var toplamMetre = toplamCm / 100m * adet;

        return toplamMetre * BantMetreFiyati;
    }

    private decimal HesaplaMalzemeMaliyeti()
    {
        decimal toplam = 0;

        foreach (var satir in Malzemeler)
        {
            if (satir.BirParcaKullanimMiktari <= 0)
                continue;

            var adet = UretimAdedi > 0 ? UretimAdedi : 1;

            satir.ToplamKullanimMiktari = satir.BirParcaKullanimMiktari * adet;

            if (satir.BirimMaliyet <= 0 && satir.StokUrunId > 0)
            {
                satir.BirimMaliyet = SonAlisFiyatiGetir(satir.StokUrunId);
            }

            satir.ToplamMaliyet = satir.ToplamKullanimMiktari * satir.BirimMaliyet;
            toplam += satir.ToplamMaliyet;
        }

        return toplam;
    }

    private decimal SonAlisFiyatiGetir(int stokUrunId)
    {
        var sonGiris = _db.StokHareketleri
            .Where(x =>
                x.StokUrunId == stokUrunId &&
                x.Tip == StokHareketTipi.Giris)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .FirstOrDefault();

        return sonGiris?.KdvDahilBirimFiyat ?? 0;
    }

    private async Task<decimal> StokMiktariGetirAsync(int firmaId, int stokUrunId)
    {
        var giris = await _db.StokHareketleri
            .Where(x =>
                x.FirmaId == firmaId &&
                x.StokUrunId == stokUrunId &&
                x.Tip == StokHareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Miktar) ?? 0;

        var cikis = await _db.StokHareketleri
            .Where(x =>
                x.FirmaId == firmaId &&
                x.StokUrunId == stokUrunId &&
                x.Tip == StokHareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Miktar) ?? 0;

        return giris - cikis;
    }

    private async Task StoklariYukleAsync(int firmaId)
    {
        StokUrunleri = await _db.StokUrunler
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.Ad)
            .ToListAsync();
    }

    private void FormuHazirla()
    {
        UretimAdedi = 1;
        BantUstAlt = true;
        BantSagSol = true;
        KdvVarsayilanMalzemeSatirlari();
    }

    private void MalzemeSatirlariniTamamla()
    {
        Malzemeler ??= new List<MalzemeSatiri>();

        while (Malzemeler.Count < 5)
            Malzemeler.Add(new MalzemeSatiri());
    }

    private void KdvVarsayilanMalzemeSatirlari()
    {
        Malzemeler = Enumerable
            .Range(0, 5)
            .Select(_ => new MalzemeSatiri())
            .ToList();
    }
}

public class MalzemeSatiri
{
    public int StokUrunId { get; set; }
    public decimal BirParcaKullanimMiktari { get; set; }
    public decimal BirimMaliyet { get; set; }
    public decimal ToplamKullanimMiktari { get; set; }
    public decimal ToplamMaliyet { get; set; }
}