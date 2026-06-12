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
        Yeni.KalemAciklama = (Yeni.KalemAciklama ?? "").Trim();

        if (Yeni.CariKartId <= 0)
            ModelState.AddModelError("", "Cari seçimi zorunludur.");

        if (string.IsNullOrWhiteSpace(Yeni.KalemAciklama))
            ModelState.AddModelError("", "Fatura açıklaması zorunludur.");

        if (Yeni.Miktar <= 0)
            ModelState.AddModelError("", "Miktar sıfırdan büyük olmalıdır.");

        if (Yeni.BirimFiyat <= 0)
            ModelState.AddModelError("", "Birim fiyat sıfırdan büyük olmalıdır.");

        var cariVarMi = await _db.CariKartlar.AnyAsync(x => x.Id == Yeni.CariKartId && x.FirmaId == firmaId.Value);
        if (!cariVarMi)
            ModelState.AddModelError("", "Seçilen cari bulunamadı.");

        if (!ModelState.IsValid)
        {
            await VerileriYukleAsync(firmaId.Value);
            return Page();
        }

        var araToplam = Yeni.Miktar * Yeni.BirimFiyat;
        var kdvTutar = araToplam * Yeni.KdvOrani / 100m;
        var genelToplam = araToplam + kdvTutar;
        var faturaNo = string.IsNullOrWhiteSpace(Yeni.FaturaNo)
            ? $"FTR-{DateTime.Now:yyyyMMdd-HHmmss}"
            : Yeni.FaturaNo.Trim();

        var fatura = new Fatura
        {
            FirmaId = firmaId.Value,
            CariKartId = Yeni.CariKartId,
            FaturaNo = faturaNo,
            Tip = Yeni.Tip,
            Tarih = Yeni.Tarih.Date,
            VadeTarihi = Yeni.VadeTarihi?.Date,
            AraToplam = araToplam,
            KdvToplam = kdvTutar,
            GenelToplam = genelToplam,
            OdenenToplam = 0,
            Aciklama = Yeni.Aciklama,
            OlusturmaTarihi = DateTime.UtcNow,
            Kalemler = new List<FaturaKalem>
            {
                new()
                {
                    Aciklama = Yeni.KalemAciklama,
                    Miktar = Yeni.Miktar,
                    BirimFiyat = Yeni.BirimFiyat,
                    KdvOrani = Yeni.KdvOrani,
                    AraToplam = araToplam,
                    KdvTutar = kdvTutar,
                    GenelToplam = genelToplam
                }
            }
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
            Tarih = Odeme.Tarih.Date,
            Tip = kasaTipi,
            Tutar = islenecekTutar,
            Aciklama = $"{islemAdi} - {fatura.FaturaNo} - {fatura.CariKart?.Unvan}"
        });

        await _db.SaveChangesAsync();

        TempData["Basari"] = $"{islemAdi} kaydedildi ve kasaya işlendi.";
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

        if (Yeni.Tarih == default)
            Yeni.Tarih = DateTime.UtcNow.Date;

        if (Yeni.Miktar <= 0)
            Yeni.Miktar = 1;

        if (Yeni.KdvOrani <= 0)
            Yeni.KdvOrani = 20;

        if (Odeme.Tarih == default)
            Odeme.Tarih = DateTime.UtcNow.Date;
    }

    public class FaturaForm
    {
        public int CariKartId { get; set; }
        public string FaturaNo { get; set; } = "";
        public FaturaTipi Tip { get; set; } = FaturaTipi.Satis;
        public DateTime Tarih { get; set; } = DateTime.UtcNow.Date;
        public DateTime? VadeTarihi { get; set; }
        public string KalemAciklama { get; set; } = "";
        public decimal Miktar { get; set; } = 1;
        public decimal BirimFiyat { get; set; }
        public decimal KdvOrani { get; set; } = 20;
        public string Aciklama { get; set; } = "";
    }

    public class OdemeForm
    {
        public int FaturaId { get; set; }
        public decimal Tutar { get; set; }
        public DateTime Tarih { get; set; } = DateTime.UtcNow.Date;
    }
}