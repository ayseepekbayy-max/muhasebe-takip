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
    public decimal UretimAdedi { get; set; } = 1;

    [BindProperty]
    public List<PlakaSatiri> Plakalar { get; set; } = new();

    [BindProperty]
    public List<BantlamaSatiri> Bantlamalar { get; set; } = new();

    [BindProperty]
    public List<PlakaSatiri> Arkaliklar { get; set; } = new();

    [BindProperty]
    public List<MalzemeSatiri> Malzemeler { get; set; } = new();

    [BindProperty]
    public bool StoktanDus { get; set; }

    public List<StokUrun> StokUrunleri { get; set; } = new();

    public decimal PlakaMaliyeti { get; set; }
    public decimal ArkalikMaliyeti { get; set; }
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
        SatirlariTamamla();
        Hesapla();

        await MaliyetKaydiOlusturAsync(firmaId.Value);

        HesaplandiMi = true;
        Mesaj = "Maliyet hesabı kaydedildi.";
        return Page();
    }

    public async Task<IActionResult> OnPostStokDusAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await StoklariYukleAsync(firmaId.Value);
        SatirlariTamamla();
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

        await MaliyetKaydiOlusturAsync(firmaId.Value);

        await _db.SaveChangesAsync();

        Mesaj = "Maliyet hesabı kaydedildi ve malzemeler stoktan düşüldü.";
        return Page();
    }

    private void Hesapla()
    {
        UretimAdi = (UretimAdi ?? "").Trim();

        if (UretimAdedi <= 0)
            UretimAdedi = 1;

        PlakaMaliyeti = HesaplaPlakaGrubu(Plakalar);
        BantlamaMaliyeti = HesaplaBantlamaMaliyeti();
        ArkalikMaliyeti = HesaplaPlakaGrubu(Arkaliklar);
        MalzemeMaliyeti = HesaplaMalzemeMaliyeti();

        ToplamMaliyet =
            PlakaMaliyeti +
            BantlamaMaliyeti +
            ArkalikMaliyeti +
            MalzemeMaliyeti;

        BirimMaliyet =
            UretimAdedi > 0
                ? ToplamMaliyet / UretimAdedi
                : 0;
    }
    
    private async Task MaliyetKaydiOlusturAsync(int firmaId)
{
    if (string.IsNullOrWhiteSpace(UretimAdi))
        return;

    if (ToplamMaliyet <= 0)
        return;

    var kayit = new MaliyetKaydi
    {
        FirmaId = firmaId,
        UretimAdi = UretimAdi.Trim(),
        UretimAdedi = UretimAdedi,
        PlakaMaliyeti = PlakaMaliyeti,
        BantlamaMaliyeti = BantlamaMaliyeti,
        ArkalikMaliyeti = ArkalikMaliyeti,
        MalzemeMaliyeti = MalzemeMaliyeti,
        ToplamMaliyet = ToplamMaliyet,
        BirimMaliyet = BirimMaliyet,
        HesapTarihi = DateTime.UtcNow
    };

    _db.MaliyetKayitlari.Add(kayit);

    await _db.SaveChangesAsync();
}
    private decimal HesaplaPlakaGrubu(List<PlakaSatiri> satirlar)
    {
        decimal toplam = 0;

        foreach (var satir in satirlar)
        {
            satir.Aciklama = (satir.Aciklama ?? "").Trim();
            satir.PlakaMaliyeti = 0;
            satir.ToplamMaliyet = 0;

            if (
                satir.PlakaEnCm <= 0 ||
                satir.PlakaBoyCm <= 0 ||
                satir.PlakaFiyati <= 0 ||
                satir.ParcaEnCm <= 0 ||
                satir.ParcaBoyCm <= 0 ||
                satir.ParcaAdedi <= 0
            )
            {
                continue;
            }

            var plakaAlanM2 =
                (satir.PlakaEnCm * satir.PlakaBoyCm) / 10000m;

            var parcaAlanM2 =
                (satir.ParcaEnCm * satir.ParcaBoyCm) / 10000m;

            var toplamParcaAlanM2 =
                parcaAlanM2 * satir.ParcaAdedi;

            if (plakaAlanM2 <= 0)
                continue;

            satir.PlakaMaliyeti =
                toplamParcaAlanM2 / plakaAlanM2 * satir.PlakaFiyati;

            satir.ToplamMaliyet = satir.PlakaMaliyeti;

            toplam += satir.PlakaMaliyeti;
        }

        return toplam;
    }

    private decimal HesaplaBantlamaMaliyeti()
    {
        decimal toplam = 0;

        foreach (var satir in Bantlamalar)
        {
            satir.Aciklama = (satir.Aciklama ?? "").Trim();
            satir.ToplamMetre = 0;
            satir.ToplamMaliyet = 0;

            if (
                satir.ParcaEnCm <= 0 ||
                satir.ParcaBoyCm <= 0 ||
                satir.ParcaAdedi <= 0 ||
                satir.BantMetreFiyati <= 0
            )
            {
                continue;
            }

            decimal toplamCm = 0;

            if (satir.BantUstAlt)
                toplamCm += satir.ParcaEnCm * 2;

            if (satir.BantSagSol)
                toplamCm += satir.ParcaBoyCm * 2;

            if (!satir.BantUstAlt && !satir.BantSagSol)
                toplamCm = (satir.ParcaEnCm + satir.ParcaBoyCm) * 2;

            satir.ToplamMetre =
                toplamCm / 100m * satir.ParcaAdedi;

            satir.ToplamMaliyet =
                satir.ToplamMetre * satir.BantMetreFiyati;

            toplam += satir.ToplamMaliyet;
        }

        return toplam;
    }
        private decimal HesaplaMalzemeMaliyeti()
    {
        decimal toplam = 0;

        foreach (var satir in Malzemeler)
        {
            satir.ToplamKullanimMiktari = 0;
            satir.ToplamMaliyet = 0;

            if (satir.BirParcaKullanimMiktari <= 0)
                continue;

            satir.ToplamKullanimMiktari =
                satir.BirParcaKullanimMiktari * UretimAdedi;

            if (satir.BirimMaliyet <= 0 && satir.StokUrunId > 0)
                satir.BirimMaliyet = SonAlisFiyatiGetir(satir.StokUrunId);

            satir.ToplamMaliyet =
                satir.ToplamKullanimMiktari * satir.BirimMaliyet;

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

        Plakalar = Enumerable.Range(0, 4)
            .Select(_ => new PlakaSatiri())
            .ToList();

        Bantlamalar = Enumerable.Range(0, 1)
            .Select(_ => new BantlamaSatiri())
            .ToList();

        Arkaliklar = Enumerable.Range(0, 1)
            .Select(_ => new PlakaSatiri())
            .ToList();

        Malzemeler = Enumerable.Range(0, 8)
            .Select(_ => new MalzemeSatiri())
            .ToList();
    }

    private void SatirlariTamamla()
    {
        Plakalar ??= new List<PlakaSatiri>();
        Bantlamalar ??= new List<BantlamaSatiri>();
        Arkaliklar ??= new List<PlakaSatiri>();
        Malzemeler ??= new List<MalzemeSatiri>();

        if (!Plakalar.Any())
            Plakalar.Add(new PlakaSatiri());

        if (!Bantlamalar.Any())
            Bantlamalar.Add(new BantlamaSatiri());

        if (!Arkaliklar.Any())
            Arkaliklar.Add(new PlakaSatiri());

        while (Malzemeler.Count < 8)
            Malzemeler.Add(new MalzemeSatiri());
    }
}

public class PlakaSatiri
{
    public string Aciklama { get; set; } = "";

    public decimal PlakaEnCm { get; set; }

    public decimal PlakaBoyCm { get; set; }

    public decimal PlakaFiyati { get; set; }

    public decimal ParcaEnCm { get; set; }

    public decimal ParcaBoyCm { get; set; }

    public decimal ParcaAdedi { get; set; }

    public decimal PlakaMaliyeti { get; set; }

    public decimal ToplamMaliyet { get; set; }
}

public class BantlamaSatiri
{
    public string Aciklama { get; set; } = "";

    public decimal ParcaEnCm { get; set; }

    public decimal ParcaBoyCm { get; set; }

    public decimal ParcaAdedi { get; set; }

    public decimal BantMetreFiyati { get; set; }

    public bool BantUstAlt { get; set; } = true;

    public bool BantSagSol { get; set; } = true;

    public decimal ToplamMetre { get; set; }

    public decimal ToplamMaliyet { get; set; }
}

public class MalzemeSatiri
{
    public int StokUrunId { get; set; }

    public decimal BirParcaKullanimMiktari { get; set; }

    public decimal BirimMaliyet { get; set; }

    public decimal ToplamKullanimMiktari { get; set; }

    public decimal ToplamMaliyet { get; set; }
}