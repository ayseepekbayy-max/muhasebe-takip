using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Helpers;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Calisanlar.Detay;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public IndexModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public Calisan? Calisan { get; set; }

    public List<CalisanAvans> Kayitlar { get; set; } = new();

    public List<CalisanMaasArsiv> Arsivler { get; set; } = new();

    public List<CalisanAvans> SeciliArsivDetaylari { get; set; } = new();

    public int? SeciliArsivId { get; set; }

    public decimal ToplamMaas { get; set; }
    public decimal ToplamAvans { get; set; }

    public decimal Kalan
    {
        get
        {
            var kalan = ToplamMaas - ToplamAvans;
            return kalan < 0 ? 0 : kalan;
        }
    }

    public DateTime DonemBaslangic { get; set; }
    public DateTime DonemBitis { get; set; }

    [BindProperty]
    public DateTime Tarih { get; set; } = DateTime.UtcNow.Date;

    [BindProperty]
    public decimal Tutar { get; set; }

    [BindProperty]
    public string? Aciklama { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, int? arsivId)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value, arsivId);

        if (Calisan == null)
            return NotFound();

        Tarih = DateTime.UtcNow.Date;
        Tutar = 0;
        Aciklama = "";

        return Page();
    }

    public async Task<IActionResult> OnPostAvansAsync(int id)
    {
        return await KayitEkleAsync(id, CalisanHareketTipi.Avans);
    }

    public async Task<IActionResult> OnPostMaasAsync(int id)
    {
        return await KayitEkleAsync(id, CalisanHareketTipi.MaasOdeme);
    }

    public async Task<IActionResult> OnPostDigerAsync(int id)
    {
        return await KayitEkleAsync(id, CalisanHareketTipi.Diger);
    }

    private async Task<IActionResult> KayitEkleAsync(int id, CalisanHareketTipi tip)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value, null);

        if (Calisan == null)
            return NotFound();

        if (Tutar <= 0)
        {
            ModelState.AddModelError("", "Tutar 0'dan büyük olmalı.");
            return Page();
        }

        var utcTarih = Tarih.Kind switch
        {
            DateTimeKind.Utc => Tarih.Date,
            DateTimeKind.Local => Tarih.ToUniversalTime().Date,
            _ => DateTime.SpecifyKind(Tarih.Date, DateTimeKind.Utc)
        };

        var kayit = new CalisanAvans
        {
            FirmaId = firmaId.Value,
            CalisanId = id,
            Tarih = utcTarih,
            Tutar = Tutar,
            Aciklama = (Aciklama ?? "").Trim(),
            Tip = tip,
            ArsivlendiMi = false
        };

        _db.CalisanAvanslari.Add(kayit);
        var modul = tip == CalisanHareketTipi.Avans ? "Avans" : "Maaş";
        var islemAdi = tip == CalisanHareketTipi.Avans ? "avans" : "maaş hareketi";
        await _islemGecmisi.KaydetAsync(
            modul,
            "Ekleme",
            $"{Calisan.AdSoyad} çalışanına {Tutar:N2} TL {islemAdi} eklendi.",
            yeniDeger: IslemGecmisiSnapshots.CalisanHareket(kayit));
        await _db.SaveChangesAsync();

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostOdendiAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var calisan = await _db.Calisanlar
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);

        if (calisan == null)
            return NotFound();

        var aktifKayitlar = await _db.CalisanAvanslari
            .Where(x =>
                x.CalisanId == id &&
                x.FirmaId == firmaId.Value &&
                !x.ArsivlendiMi)
            .ToListAsync();

        if (aktifKayitlar.Count == 0)
        {
            await YukleAsync(id, firmaId.Value, null);
            ModelState.AddModelError("", "Arşivlenecek kayıt yok.");
            return Page();
        }

        var toplamMaas = aktifKayitlar
        .Where(x =>
            x.Tip == CalisanHareketTipi.MaasOdeme ||
            x.Tip == CalisanHareketTipi.Diger)
        .Sum(x => x.Tutar);


        var toplamAvans = aktifKayitlar
            .Where(x => x.Tip == CalisanHareketTipi.Avans)
            .Sum(x => x.Tutar);

        var kalan = toplamMaas - toplamAvans;
        if (kalan < 0)
            kalan = 0;

        var arsiv = new CalisanMaasArsiv
        {
            FirmaId = firmaId.Value,
            CalisanId = id,
            DonemBaslangic = aktifKayitlar.Min(x => x.Tarih),
            DonemBitis = aktifKayitlar.Max(x => x.Tarih),
            ToplamMaas = toplamMaas,
            ToplamAvans = toplamAvans,
            KalanMaas = kalan,
            OdemeTarihi = DateTime.UtcNow,
            Aciklama = "Manuel arşivleme"
        };

        _db.CalisanMaasArsivleri.Add(arsiv);

        foreach (var kayit in aktifKayitlar)
        {
            kayit.ArsivlendiMi = true;
        }

        await _islemGecmisi.KaydetAsync(
            "Maaş",
            "Ödeme",
            $"{calisan.AdSoyad} çalışanının {kalan:N2} TL kalan maaş ödemesi tamamlandı.",
            eskiDeger: aktifKayitlar.Select(IslemGecmisiSnapshots.CalisanHareket).ToList(),
            yeniDeger: IslemGecmisiSnapshots.MaasArsiv(arsiv));
        await _db.SaveChangesAsync();

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSilAsync(int id, int id2)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var kayit = await _db.CalisanAvanslari
            .FirstOrDefaultAsync(x =>
                x.Id == id2 &&
                x.CalisanId == id &&
                x.FirmaId == firmaId.Value &&
                !x.ArsivlendiMi);

        if (kayit != null)
        {
            var modul = kayit.Tip == CalisanHareketTipi.Avans ? "Avans" : "Maaş";
            var islemAdi = kayit.Tip == CalisanHareketTipi.Avans ? "avans" : "maaş hareketi";
            await _islemGecmisi.KaydetAsync(
                modul,
                "Silme",
                $"{islemAdi} silindi (ID: {kayit.Id}).",
                eskiDeger: IslemGecmisiSnapshots.CalisanHareket(kayit));
            _db.CalisanAvanslari.Remove(kayit);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostArsivGeriAcAsync(int id, int arsivId)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var arsiv = await _db.CalisanMaasArsivleri
            .FirstOrDefaultAsync(x =>
                x.Id == arsivId &&
                x.CalisanId == id &&
                x.FirmaId == firmaId.Value);

        if (arsiv == null)
            return NotFound();

        var kayitlar = await _db.CalisanAvanslari
            .Where(x =>
                x.CalisanId == id &&
                x.FirmaId == firmaId.Value &&
                x.ArsivlendiMi &&
                x.Tarih >= arsiv.DonemBaslangic &&
                x.Tarih <= arsiv.DonemBitis)
            .ToListAsync();

        foreach (var kayit in kayitlar)
        {
            kayit.ArsivlendiMi = false;
        }

        await _islemGecmisi.KaydetAsync(
            "Maaş",
            "Silme",
            $"Maaş ödeme kaydı geri açılarak silindi (ID: {arsiv.Id}).",
            eskiDeger: IslemGecmisiSnapshots.MaasArsiv(arsiv),
            yeniDeger: kayitlar.Select(IslemGecmisiSnapshots.CalisanHareket).ToList());
        _db.CalisanMaasArsivleri.Remove(arsiv);

        await _db.SaveChangesAsync();

        return RedirectToPage(new { id });
    }

    private async Task YukleAsync(int id, int firmaId, int? arsivId)
    {
        Calisan = await _db.Calisanlar
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (Calisan == null)
            return;

        var donem = MaasDonemiHelper.GetDonem(DateTime.UtcNow.Date);
        DonemBaslangic = DateTime.SpecifyKind(donem.Baslangic, DateTimeKind.Utc);
        DonemBitis = DateTime.SpecifyKind(donem.Bitis, DateTimeKind.Utc);

        Kayitlar = await _db.CalisanAvanslari
            .Where(x =>
                x.CalisanId == id &&
                x.FirmaId == firmaId &&
                !x.ArsivlendiMi)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();

        Arsivler = await _db.CalisanMaasArsivleri
            .Where(x => x.CalisanId == id && x.FirmaId == firmaId)
            .OrderByDescending(x => x.OdemeTarihi)
            .ToListAsync();

        ToplamMaas = await _db.CalisanAvanslari
        .Where(x =>
            x.CalisanId == id &&
            x.FirmaId == firmaId &&
            !x.ArsivlendiMi &&
            (
                x.Tip == CalisanHareketTipi.MaasOdeme ||
                x.Tip == CalisanHareketTipi.Diger
            ))
        .SumAsync(x => (decimal?)x.Tutar) ?? 0;



        ToplamAvans = await _db.CalisanAvanslari
            .Where(x =>
                x.CalisanId == id &&
                x.FirmaId == firmaId &&
                !x.ArsivlendiMi &&
                x.Tip == CalisanHareketTipi.Avans)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        SeciliArsivDetaylari = new List<CalisanAvans>();
        SeciliArsivId = arsivId;

        if (arsivId.HasValue)
        {
            var seciliArsiv = await _db.CalisanMaasArsivleri
                .FirstOrDefaultAsync(x =>
                    x.Id == arsivId.Value &&
                    x.CalisanId == id &&
                    x.FirmaId == firmaId);

            if (seciliArsiv != null)
            {
                SeciliArsivDetaylari = await _db.CalisanAvanslari
                    .Where(x =>
                        x.CalisanId == id &&
                        x.FirmaId == firmaId &&
                        x.ArsivlendiMi &&
                        x.Tarih >= seciliArsiv.DonemBaslangic &&
                        x.Tarih <= seciliArsiv.DonemBitis)
                    .OrderBy(x => x.Tarih)
                    .ThenBy(x => x.Id)
                    .ToListAsync();
            }
        }
    }
}
