using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Helpers;

namespace MuhasebeTakip2.App.Pages.Calisanlar.Detay;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
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
    public DateTime Tarih { get; set; } = DateTime.Today;

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

        Tarih = DateTime.Today;
        Tutar = 0;
        Aciklama = "";

        return Page();
    }

    public async Task<IActionResult> OnPostAvansAsync(int id)
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

        var kayit = new CalisanAvans
        {
            FirmaId = firmaId.Value,
            CalisanId = id,
            Tarih = Tarih,
            Tutar = Tutar,
            Aciklama = (Aciklama ?? "").Trim(),
            Tip = CalisanHareketTipi.Avans,
            ArsivlendiMi = false
        };

        _db.CalisanAvanslari.Add(kayit);
        await _db.SaveChangesAsync();

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMaasAsync(int id)
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

        var kayit = new CalisanAvans
        {
            FirmaId = firmaId.Value,
            CalisanId = id,
            Tarih = Tarih,
            Tutar = Tutar,
            Aciklama = (Aciklama ?? "").Trim(),
            Tip = CalisanHareketTipi.MaasOdeme,
            ArsivlendiMi = false
        };

        _db.CalisanAvanslari.Add(kayit);
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

        // ARTIK TARİHE GÖRE OTOMATİK DÖNEM YOK
        // SADECE BUTONA BASILDIĞINDA TÜM AKTİF KAYITLAR ARŞİVLENİR
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
            .Where(x => x.Tip == CalisanHareketTipi.MaasOdeme)
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
            OdemeTarihi = DateTime.Now,
            Aciklama = "Manuel arşivleme"
        };

        _db.CalisanMaasArsivleri.Add(arsiv);

        foreach (var kayit in aktifKayitlar)
        {
            kayit.ArsivlendiMi = true;
        }

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
            _db.CalisanAvanslari.Remove(kayit);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage(new { id });
    }

    private async Task YukleAsync(int id, int firmaId, int? arsivId)
    {
        Calisan = await _db.Calisanlar
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (Calisan == null)
            return;

        // GÖRÜNTÜLEMEDE DÖNEM BİLGİSİ KALSIN İSTERSEN DURSUN
        // AMA ARŞİVLEMEYİ ETKİLEMEZ
        var donem = MaasDonemiHelper.GetDonem(DateTime.Today);
        DonemBaslangic = donem.Baslangic;
        DonemBitis = donem.Bitis;

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

        // ÜSTTEKİ ÖZETTE TÜM AKTİF KAYITLAR GÖZÜKSÜN
        // ARTIK GÜNE/DÖNEME GÖRE AYRILMASIN
        ToplamMaas = await _db.CalisanAvanslari
            .Where(x =>
                x.CalisanId == id &&
                x.FirmaId == firmaId &&
                !x.ArsivlendiMi &&
                x.Tip == CalisanHareketTipi.MaasOdeme)
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