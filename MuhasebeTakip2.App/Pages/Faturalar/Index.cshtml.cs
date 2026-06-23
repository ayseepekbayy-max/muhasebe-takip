using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Faturalar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Fatura> Faturalar { get; set; } = new();
    public List<CariKart> Cariler { get; set; } = new();

    public decimal ToplamSatis { get; set; }
    public decimal ToplamAlis { get; set; }
    public decimal BekleyenTahsilat { get; set; }
    public decimal BekleyenOdeme { get; set; }

    [BindProperty]
    public FaturaForm Yeni { get; set; } = new();

    [BindProperty]
    public OdemeForm Odeme { get; set; } = new();

    [BindProperty]
    public NumaraAyariForm NumaraAyari { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await VerileriYukleAsync(firmaId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostEkleAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Yeni.Aciklama = (Yeni.Aciklama ?? "").Trim();
        Yeni.Kalemler ??= new List<FaturaKalemForm>();

        if (Yeni.CariKartId <= 0)
            ModelState.AddModelError("", "Cari seçimi zorunludur.");

        var cariVarMi = await _db.CariKartlar.AnyAsync(x => x.Id == Yeni.CariKartId && x.FirmaId == firmaId.Value);
        if (!cariVarMi)
            ModelState.AddModelError("", "Seçilen cari bulunamadı.");

        var doluKalemler = TemizKalemler(Yeni.Kalemler);
        KalemleriDogrula(doluKalemler);

        if (!ModelState.IsValid)
        {
            Yeni.Kalemler = doluKalemler.Any() ? doluKalemler : Yeni.Kalemler;
            await VerileriYukleAsync(firmaId.Value);
            return Page();
        }

        var faturaKalemleri = FaturaKalemleriOlustur(doluKalemler);
        var faturaNo = string.IsNullOrWhiteSpace(Yeni.FaturaNo)
            ? await SiradakiFaturaNoAsync(firmaId.Value)
            : Yeni.FaturaNo.Trim();

        var fatura = new Fatura
        {
            FirmaId = firmaId.Value,
            CariKartId = Yeni.CariKartId,
            FaturaNo = faturaNo,
            Tip = Yeni.Tip,
            Tarih = ToUtcDate(Yeni.Tarih),
            VadeTarihi = Yeni.VadeTarihi.HasValue ? ToUtcDate(Yeni.VadeTarihi.Value) : null,
            AraToplam = faturaKalemleri.Sum(x => x.AraToplam),
            KdvToplam = faturaKalemleri.Sum(x => x.KdvTutar),
            GenelToplam = faturaKalemleri.Sum(x => x.GenelToplam),
            OdenenToplam = 0,
            Aciklama = Yeni.Aciklama,
            OlusturmaTarihi = DateTime.UtcNow,
            Kalemler = faturaKalemleri
        };

        _db.Faturalar.Add(fatura);
        await _db.SaveChangesAsync();

        TempData["Basari"] = "Fatura oluşturuldu.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostOdemeAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        if (Odeme.FaturaId <= 0 || Odeme.Tutar <= 0)
        {
            TempData["Hata"] = "Ödeme/tahsilat tutarı sıfırdan büyük olmalıdır.";
            return RedirectToPage();
        }

        var fatura = await _db.Faturalar
            .Include(x => x.CariKart)
            .FirstOrDefaultAsync(x => x.Id == Odeme.FaturaId && x.FirmaId == firmaId.Value);

        if (fatura == null)
        {
            TempData["Hata"] = "Fatura bulunamadı.";
            return RedirectToPage();
        }

        var kalan = fatura.GenelToplam - fatura.OdenenToplam;
        if (kalan <= 0)
        {
            TempData["Hata"] = "Bu fatura zaten kapalı.";
            return RedirectToPage();
        }

        var islenecekTutar = Math.Min(Odeme.Tutar, kalan);
        fatura.OdenenToplam += islenecekTutar;

        var kasaTipi = fatura.Tip == FaturaTipi.Satis ? HareketTipi.Giris : HareketTipi.Cikis;
        var islemAdi = fatura.Tip == FaturaTipi.Satis ? "Tahsilat" : "Ödeme";

        _db.KasaHareketleri.Add(new KasaHareket
        {
            FirmaId = firmaId.Value,
            CariKartId = fatura.CariKartId,
            FaturaId = fatura.Id,
            Tarih = ToUtcDate(Odeme.Tarih),
            Tip = kasaTipi,
            Tutar = islenecekTutar,
            Aciklama = $"{islemAdi} - {fatura.FaturaNo} - {fatura.CariKart?.Unvan}"
        });

        await _db.SaveChangesAsync();

        TempData["Basari"] = $"{islemAdi} kaydedildi ve kasaya işlendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSilAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var fatura = await _db.Faturalar
            .Include(x => x.Kalemler)
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);

        if (fatura == null)
        {
            TempData["Hata"] = "Fatura bulunamadı.";
            return RedirectToPage();
        }

        _db.Faturalar.Remove(fatura);
        await _db.SaveChangesAsync();

        TempData["Basari"] = "Fatura silindi.";
        return RedirectToPage();
    }

    private async Task VerileriYukleAsync(int firmaId)
    {
        Cariler = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.Unvan)
            .ToListAsync();

        Faturalar = await _db.Faturalar
            .Include(x => x.CariKart)
            .Include(x => x.Kalemler)
            .Where(x => x.FirmaId == firmaId)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        ToplamSatis = Faturalar.Where(x => x.Tip == FaturaTipi.Satis).Sum(x => x.GenelToplam);
        ToplamAlis = Faturalar.Where(x => x.Tip == FaturaTipi.Alis).Sum(x => x.GenelToplam);
        BekleyenTahsilat = Faturalar.Where(x => x.Tip == FaturaTipi.Satis).Sum(x => Math.Max(0, x.KalanTutar));
        BekleyenOdeme = Faturalar.Where(x => x.Tip == FaturaTipi.Alis).Sum(x => Math.Max(0, x.KalanTutar));

        var ayar = await GetOrCreateNumaraAyariAsync(firmaId);
        NumaraAyari = new NumaraAyariForm
        {
            Prefix = ayar.Prefix,
            SonNumara = ayar.SonNumara,
            SiraUzunlugu = ayar.SiraUzunlugu,
            YilEkle = ayar.YilEkle,
            SiradakiOrnek = NumaraOlustur(ayar, ayar.SonNumara + 1)
        };

        if (Yeni.Tarih == default)
            Yeni.Tarih = DateTime.UtcNow.Date;

        Yeni.Kalemler ??= new List<FaturaKalemForm>();
        if (!Yeni.Kalemler.Any())
            Yeni.Kalemler.Add(new FaturaKalemForm());

        foreach (var kalem in Yeni.Kalemler)
        {
            if (kalem.Miktar <= 0)
                kalem.Miktar = 1;

            if (kalem.KdvOrani <= 0)
                kalem.KdvOrani = 20;
        }

        if (Odeme.Tarih == default)
            Odeme.Tarih = DateTime.UtcNow.Date;
    }

    private async Task<string> SiradakiFaturaNoAsync(int firmaId)
    {
        var ayar = await GetOrCreateNumaraAyariAsync(firmaId);
        ayar.SonNumara += 1;
        return NumaraOlustur(ayar, ayar.SonNumara);
    }

    private async Task<FaturaNumaraAyari> GetOrCreateNumaraAyariAsync(int firmaId)
    {
        var ayar = await _db.FaturaNumaraAyarlari.FirstOrDefaultAsync(x => x.FirmaId == firmaId);
        if (ayar != null)
            return ayar;

        ayar = new FaturaNumaraAyari { FirmaId = firmaId, Prefix = "FTR", SonNumara = 0, SiraUzunlugu = 4, YilEkle = true };
        _db.FaturaNumaraAyarlari.Add(ayar);
        await _db.SaveChangesAsync();
        return ayar;
    }

    private static string NumaraOlustur(FaturaNumaraAyari ayar, int sira)
    {
        var parcalar = new List<string> { ayar.Prefix };
        if (ayar.YilEkle)
            parcalar.Add(DateTime.Now.Year.ToString());

        parcalar.Add(sira.ToString().PadLeft(ayar.SiraUzunlugu, '0'));
        return string.Join("-", parcalar);
    }

    internal static List<FaturaKalemForm> TemizKalemler(IEnumerable<FaturaKalemForm> kalemler)
    {
        return kalemler
            .Select(x => new FaturaKalemForm
            {
                Aciklama = (x.Aciklama ?? "").Trim(),
                Miktar = x.Miktar,
                BirimFiyat = x.BirimFiyat,
                KdvOrani = x.KdvOrani
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Aciklama) || x.BirimFiyat > 0)
            .ToList();
    }

    private void KalemleriDogrula(List<FaturaKalemForm> kalemler)
    {
        if (!kalemler.Any())
            ModelState.AddModelError("", "En az bir fatura kalemi girilmelidir.");

        for (var i = 0; i < kalemler.Count; i++)
        {
            var kalem = kalemler[i];
            var satir = i + 1;

            if (string.IsNullOrWhiteSpace(kalem.Aciklama))
                ModelState.AddModelError("", $"{satir}. kalem açıklaması zorunludur.");

            if (kalem.Miktar <= 0)
                ModelState.AddModelError("", $"{satir}. kalem miktarı sıfırdan büyük olmalıdır.");

            if (kalem.BirimFiyat <= 0)
                ModelState.AddModelError("", $"{satir}. kalem birim fiyatı sıfırdan büyük olmalıdır.");

            if (kalem.KdvOrani < 0)
                ModelState.AddModelError("", $"{satir}. kalem KDV oranı negatif olamaz.");
        }
    }

    internal static List<FaturaKalem> FaturaKalemleriOlustur(IEnumerable<FaturaKalemForm> kalemler)
    {
        return kalemler.Select(kalem =>
        {
            var araToplam = kalem.Miktar * kalem.BirimFiyat;
            var kdvTutar = araToplam * kalem.KdvOrani / 100m;
            var genelToplam = araToplam + kdvTutar;

            return new FaturaKalem
            {
                Aciklama = kalem.Aciklama ?? "",
                Miktar = kalem.Miktar,
                BirimFiyat = kalem.BirimFiyat,
                KdvOrani = kalem.KdvOrani,
                AraToplam = araToplam,
                KdvTutar = kdvTutar,
                GenelToplam = genelToplam
            };
        }).ToList();
    }

    internal static DateTime ToUtcDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    public class FaturaForm
    {
        public int CariKartId { get; set; }
        public string FaturaNo { get; set; } = "";
        public FaturaTipi Tip { get; set; } = FaturaTipi.Satis;
        public DateTime Tarih { get; set; } = DateTime.UtcNow.Date;
        public DateTime? VadeTarihi { get; set; }
        public List<FaturaKalemForm> Kalemler { get; set; } = new() { new FaturaKalemForm() };
        public string? Aciklama { get; set; }
    }

    public class FaturaKalemForm
    {
        public string? Aciklama { get; set; }
        public decimal Miktar { get; set; } = 1;
        public decimal BirimFiyat { get; set; }
        public decimal KdvOrani { get; set; } = 20;
    }

    public class OdemeForm
    {
        public int FaturaId { get; set; }
        public decimal Tutar { get; set; }
        public DateTime Tarih { get; set; } = DateTime.UtcNow.Date;
    }

    public class NumaraAyariForm
    {
        public string Prefix { get; set; } = "FTR";
        public int SonNumara { get; set; }
        public int SiraUzunlugu { get; set; } = 4;
        public bool YilEkle { get; set; } = true;
        public string SiradakiOrnek { get; set; } = "";
    }
}