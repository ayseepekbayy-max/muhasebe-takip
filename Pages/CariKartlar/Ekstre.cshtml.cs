using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.CariKartlar;

public class EkstreModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public EkstreModel(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public CariKart? Cari { get; set; }
    public List<Fatura> Faturalar { get; set; } = new();
    public List<KasaHareket> KasaHareketleri { get; set; } = new();
    public List<Models.IslemGecmisi> SonIslemler { get; set; } = new();
    public List<EkstreSatiri> Satirlar { get; set; } = new();
    public List<EkDosya> Dosyalar { get; set; } = new();
    public decimal ToplamBorc { get; set; }
    public decimal ToplamAlacak { get; set; }
    public decimal Bakiye => ToplamBorc - ToplamAlacak;

    [BindProperty]
    public IFormFile? EkDosya { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        if (Cari == null)
            return NotFound();

        return Page();
    }

    public async Task<IActionResult> OnPostDosyaEkleAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var cariVarMi = await _db.CariKartlar
            .AsNoTracking()
            .AnyAsync(x => x.Id == id && x.FirmaId == firmaId.Value && x.AktifMi);

        if (!cariVarMi)
            return NotFound();

        if (EkDosya == null || EkDosya.Length == 0)
        {
            TempData["Hata"] = "Lütfen bir dosya seçin.";
            return RedirectToPage(new { id });
        }

        var uzanti = Path.GetExtension(EkDosya.FileName).ToLowerInvariant();
        var izinli = new[] { ".pdf", ".png", ".jpg", ".jpeg", ".webp", ".xlsx", ".xls", ".docx" };
        if (!izinli.Contains(uzanti))
        {
            TempData["Hata"] = "PDF, resim, Excel veya Word dosyası yükleyebilirsiniz.";
            return RedirectToPage(new { id });
        }

        var klasor = Path.Combine(_env.WebRootPath, "uploads", "cariler");
        Directory.CreateDirectory(klasor);
        var dosyaAdi = $"cari-{id}-{Guid.NewGuid():N}{uzanti}";
        var fizikselYol = Path.Combine(klasor, dosyaAdi);

        await using (var fs = System.IO.File.Create(fizikselYol))
            await EkDosya.CopyToAsync(fs);

        _db.EkDosyalar.Add(new EkDosya
        {
            FirmaId = firmaId.Value,
            CariKartId = id,
            DosyaAdi = Path.GetFileName(EkDosya.FileName),
            DosyaYolu = $"/uploads/cariler/{dosyaAdi}",
            IcerikTipi = EkDosya.ContentType,
            Boyut = EkDosya.Length,
            YuklemeTarihi = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        TempData["Basari"] = "Dosya cariye eklendi.";
        return RedirectToPage(new { id });
    }

    private async Task YukleAsync(int id, int firmaId)
    {
        Cari = await _db.CariKartlar
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId && x.AktifMi);

        if (Cari == null)
            return;

        Faturalar = await _db.Faturalar
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.AktifMi && x.CariKartId == id)
            .OrderByDescending(x => x.Tarih)
            .ToListAsync();

        KasaHareketleri = await _db.KasaHareketleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.CariKartId == id)
            .OrderByDescending(x => x.Tarih)
            .ToListAsync();

        foreach (var fatura in Faturalar.Where(x => x.Durum != FaturaDurumu.Iptal))
        {
            Satirlar.Add(new EkstreSatiri
            {
                Tarih = fatura.Tarih,
                Tur = fatura.Tip == FaturaTipi.Satis ? "Satış Faturası" : "Alış Faturası",
                Aciklama = fatura.FaturaNo,
                Borc = fatura.Tip == FaturaTipi.Satis ? fatura.GenelToplam : 0,
                Alacak = fatura.Tip == FaturaTipi.Alis ? fatura.GenelToplam : 0,
                FaturaId = fatura.Id
            });
        }

        foreach (var hareket in KasaHareketleri)
        {
            Satirlar.Add(new EkstreSatiri
            {
                Tarih = hareket.Tarih,
                Tur = hareket.Tip == HareketTipi.Giris ? "Tahsilat" : "Ödeme",
                Aciklama = hareket.Aciklama,
                Borc = hareket.Tip == HareketTipi.Cikis ? hareket.Tutar : 0,
                Alacak = hareket.Tip == HareketTipi.Giris ? hareket.Tutar : 0,
                FaturaId = hareket.FaturaId
            });
        }

        Satirlar = Satirlar
            .OrderBy(x => x.Tarih)
            .ThenBy(x => x.Tur)
            .ToList();

        decimal bakiye = 0;
        foreach (var satir in Satirlar)
        {
            bakiye += satir.Borc - satir.Alacak;
            satir.Bakiye = bakiye;
        }

        ToplamBorc = Satirlar.Sum(x => x.Borc);
        ToplamAlacak = Satirlar.Sum(x => x.Alacak);

        Dosyalar = await _db.EkDosyalar
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.CariKartId == id)
            .OrderByDescending(x => x.YuklemeTarihi)
            .ToListAsync();

        var cariIdDeseni = $"\"id\":{id}";
        var bagliCariDeseni = $"\"cariKartId\":{id}";

        SonIslemler = await _db.IslemGecmisleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId &&
                ((x.Modul == "Cari Kartlar" &&
                  ((x.EskiDeger != null && x.EskiDeger.Contains(cariIdDeseni)) ||
                   (x.YeniDeger != null && x.YeniDeger.Contains(cariIdDeseni)))) ||
                 ((x.Modul == "Faturalar" || x.Modul == "Kasa") &&
                  ((x.EskiDeger != null && x.EskiDeger.Contains(bagliCariDeseni)) ||
                   (x.YeniDeger != null && x.YeniDeger.Contains(bagliCariDeseni))))))
            .OrderByDescending(x => x.Tarih)
            .Take(10)
            .ToListAsync();
    }

    public class EkstreSatiri
    {
        public DateTime Tarih { get; set; }
        public string Tur { get; set; } = "";
        public string Aciklama { get; set; } = "";
        public decimal Borc { get; set; }
        public decimal Alacak { get; set; }
        public decimal Bakiye { get; set; }
        public int? FaturaId { get; set; }
    }
}
