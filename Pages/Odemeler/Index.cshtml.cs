using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Odemeler;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public IndexModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public List<OdemeListeSatiri> Odemeler { get; set; } = new();
    public List<TamamlananOdemeSatiri> TamamlananOdemeler { get; set; } = new();
    public decimal BuAyToplamOdeme { get; set; }
    public int YaklasanOdemeler { get; set; }
    public int GecikenOdemeler { get; set; }
    public decimal ToplamKalanKrediBorcu { get; set; }
    public int AktifOdemePlaniSayisi { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Gorunum { get; set; } = "Aktif";

    [BindProperty(SupportsGet = true)]
    public OdemeTuru? FiltreOdemeTuru { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? FiltreDurum { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Arama { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await VerileriYukleAsync(firmaId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostOdemeYapildiAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var odeme = await _db.OdemePlanlari.FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);
        if (odeme == null)
        {
            TempData["Hata"] = "Ödeme planı bulunamadı.";
            return RedirectToPage();
        }

        if (OdemePlanlamaService.TamamlanmisMi(odeme))
        {
            TempData["Hata"] = "Bu ödeme planının tüm taksitleri tamamlanmıştır.";
            return RedirectToPage();
        }

        if (!odeme.AktifMi)
        {
            TempData["Hata"] = "Pasif ödeme planına ödeme işlenemez.";
            return RedirectToPage();
        }

        var bugun = DateTime.UtcNow.Date;
        var ayBaslangic = new DateTime(bugun.Year, bugun.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sonrakiAy = ayBaslangic.AddMonths(1);
        var ayniAydaOdemeVar = await _db.OdemeHareketleri.AnyAsync(x =>
            x.FirmaId == firmaId.Value &&
            x.OdemePlaniId == odeme.Id &&
            x.OdemeTarihi >= ayBaslangic &&
            x.OdemeTarihi < sonrakiAy);

        if (ayniAydaOdemeVar)
        {
            TempData["Hata"] = "Bu ödeme planı için bu ay zaten ödeme kaydı var.";
            return RedirectToPage();
        }

        var eskiDeger = new
        {
            odeme.Id,
            odeme.OdemeAdi,
            odeme.KalanTaksitSayisi,
            odeme.SonrakiOdemeTarihi,
            odeme.AktifMi,
            odeme.TamamlandiMi,
            odeme.TamamlanmaTarihi,
            odeme.SonOdemeYapildiMi
        };

        if (odeme.OtomatikTaksitDusur)
            odeme.KalanTaksitSayisi = Math.Max(0, odeme.KalanTaksitSayisi - 1);

        var tamamlandi = odeme.KalanTaksitSayisi <= 0;
        odeme.SonOdemeYapildiMi = true;
        odeme.GuncellemeTarihi = DateTime.UtcNow;

        if (tamamlandi)
        {
            odeme.KalanTaksitSayisi = 0;
            odeme.TamamlandiMi = true;
            odeme.TamamlanmaTarihi = bugun;
            odeme.SonrakiOdemeTarihi = null;
            odeme.AktifMi = false;
            odeme.BildirimAktifMi = false;
        }
        else if (odeme.SonrakiOdemeTarihi.HasValue)
        {
            odeme.SonrakiOdemeTarihi = OdemePlanlamaService.SonrakiAy(odeme.SonrakiOdemeTarihi.Value, odeme.OdemeGunu);
        }

        var hareket = new OdemeHareketi
        {
            FirmaId = firmaId.Value,
            OdemePlaniId = odeme.Id,
            OdemeTarihi = bugun,
            Tutar = odeme.AylikOdemeTutari,
            Aciklama = "Ödeme yapıldı olarak işaretlendi.",
            KalanTaksitSayisi = odeme.KalanTaksitSayisi,
            OlusturmaTarihi = DateTime.UtcNow,
            OlusturanKullaniciId = HttpContext.Session.GetInt32("KullaniciId"),
            OlusturanKullaniciAdi = HttpContext.Session.GetString("KullaniciAdi")
        };

        _db.OdemeHareketleri.Add(hareket);

        await _db.SaveChangesWithAuditAsync(
            async () =>
            {
                await _islemGecmisi.KaydetAsync(
                    "Ödemeler",
                    "Ödeme",
                    $"Ödeme yapıldı: {odeme.OdemeAdi} (ID: {odeme.Id}).",
                    eskiDeger,
                    new
                    {
                        odeme.Id,
                        odeme.OdemeAdi,
                        odeme.KalanTaksitSayisi,
                        odeme.SonrakiOdemeTarihi,
                        odeme.TamamlandiMi,
                        odeme.TamamlanmaTarihi,
                        odeme.SonOdemeYapildiMi,
                        HareketId = hareket.Id
                    });

                if (tamamlandi)
                {
                    await _islemGecmisi.KaydetAsync(
                        "Ödemeler",
                        "Tamamlandı",
                        $"{odeme.OdemeAdi} ödeme planının tüm taksitleri tamamlandı.",
                        eskiDeger,
                        new
                        {
                            odeme.Id,
                            odeme.OdemeAdi,
                            odeme.KalanTaksitSayisi,
                            odeme.TamamlandiMi,
                            odeme.TamamlanmaTarihi
                        });
                }
            },
            anaKaydiOnceKaydet: true);

        TempData["Basari"] = tamamlandi
            ? $"{odeme.OdemeAdi} başarıyla tamamlandı. Tüm taksitler ödendi."
            : "Ödeme kaydedildi, kalan taksit güncellendi.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPasifeAlAsync(int id)
    {
        return await AktiflikGuncelleAsync(id, false);
    }

    public async Task<IActionResult> OnPostAktiflestirAsync(int id)
    {
        return await AktiflikGuncelleAsync(id, true);
    }

    private async Task<IActionResult> AktiflikGuncelleAsync(int id, bool aktifMi)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var odeme = await _db.OdemePlanlari.FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);
        if (odeme == null)
        {
            TempData["Hata"] = "Ödeme planı bulunamadı.";
            return RedirectToPage();
        }

        if (OdemePlanlamaService.TamamlanmisMi(odeme))
        {
            TempData["Hata"] = "Bu ödeme planının tüm taksitleri tamamlanmıştır.";
            return RedirectToPage();
        }

        var eskiDeger = new { odeme.Id, odeme.OdemeAdi, odeme.AktifMi };
        odeme.AktifMi = aktifMi;
        odeme.GuncellemeTarihi = DateTime.UtcNow;

        await _db.SaveChangesWithAuditAsync(
            () => _islemGecmisi.KaydetAsync(
                "Ödemeler",
                aktifMi ? "Aktifleştirme" : "Pasife Alma",
                aktifMi
                    ? $"Ödeme planı aktifleştirildi: {odeme.OdemeAdi} (ID: {odeme.Id})."
                    : $"Ödeme planı pasife alındı: {odeme.OdemeAdi} (ID: {odeme.Id}).",
                eskiDeger,
                new { odeme.Id, odeme.OdemeAdi, odeme.AktifMi }),
            anaKaydiOnceKaydet: false);

        TempData["Basari"] = aktifMi ? "Ödeme planı aktifleştirildi." : "Ödeme planı pasife alındı.";
        return RedirectToPage();
    }

    private async Task VerileriYukleAsync(int firmaId)
    {
        var bugun = DateTime.UtcNow.Date;
        var ayBaslangic = new DateTime(bugun.Year, bugun.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sonrakiAy = ayBaslangic.AddMonths(1);

        var sorgu = _db.OdemePlanlari
            .AsNoTracking()
            .Include(x => x.Hareketler)
            .Where(x => x.FirmaId == firmaId);

        if (FiltreOdemeTuru.HasValue)
            sorgu = sorgu.Where(x => x.OdemeTuru == FiltreOdemeTuru.Value);

        if (!string.IsNullOrWhiteSpace(Arama))
        {
            var arama = Arama.Trim().ToLower();
            sorgu = sorgu.Where(x =>
                x.OdemeAdi.ToLower().Contains(arama) ||
                (x.Aciklama != null && x.Aciklama.ToLower().Contains(arama)));
        }

        var planlar = await sorgu
            .OrderBy(x => x.SonrakiOdemeTarihi == null)
            .ThenBy(x => x.SonrakiOdemeTarihi)
            .ThenBy(x => x.OdemeAdi)
            .ToListAsync();

        var satirlar = planlar.Select(x =>
        {
            var buAyOdendi = x.Hareketler.Any(h => h.OdemeTarihi >= ayBaslangic && h.OdemeTarihi < sonrakiAy);
            var durum = OdemePlanlamaService.Durum(x, bugun, buAyOdendi);
            return new OdemeListeSatiri(x, durum, buAyOdendi);
        }).ToList();

        Odemeler = satirlar
            .Where(x => !OdemePlanlamaService.TamamlanmisMi(x.Odeme) && x.Odeme.AktifMi)
            .ToList();

        if (!string.IsNullOrWhiteSpace(FiltreDurum))
        {
            Odemeler = FiltreDurum switch
            {
                "Yaklasan" => Odemeler.Where(x => x.Durum == OdemeDurumu.Yaklasiyor || x.Durum == OdemeDurumu.Bugun).ToList(),
                "Geciken" => Odemeler.Where(x => x.Durum == OdemeDurumu.Gecikti).ToList(),
                "Pasif" => satirlar.Where(x => !OdemePlanlamaService.TamamlanmisMi(x.Odeme) && !x.Odeme.AktifMi).ToList(),
                _ => Odemeler
            };
        }

        TamamlananOdemeler = satirlar
            .Where(x => OdemePlanlamaService.TamamlanmisMi(x.Odeme))
            .Select(x => new TamamlananOdemeSatiri(
                x.Odeme,
                x.Odeme.Hareketler.Sum(h => h.Tutar),
                x.Odeme.TamamlanmaTarihi,
                x.Odeme.Hareketler.OrderByDescending(h => h.OdemeTarihi).ThenByDescending(h => h.Id).FirstOrDefault()?.OdemeTarihi))
            .OrderByDescending(x => x.TamamlanmaTarihi ?? x.SonOdemeHareketiTarihi ?? x.Odeme.OlusturmaTarihi)
            .ThenBy(x => x.Odeme.OdemeAdi)
            .ToList();

        BuAyToplamOdeme = await _db.OdemeHareketleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.OdemeTarihi >= ayBaslangic && x.OdemeTarihi < sonrakiAy)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        YaklasanOdemeler = Odemeler.Count(x => x.Durum == OdemeDurumu.Yaklasiyor || x.Durum == OdemeDurumu.Bugun);
        GecikenOdemeler = Odemeler.Count(x => x.Durum == OdemeDurumu.Gecikti);
        ToplamKalanKrediBorcu = planlar
            .Where(x => x.FirmaId == firmaId &&
                        x.AktifMi &&
                        !OdemePlanlamaService.TamamlanmisMi(x) &&
                        x.OdemeTuru is OdemeTuru.Kredi or OdemeTuru.KrediKarti)
            .Sum(x => x.KalanToplamTutar);
        AktifOdemePlaniSayisi = Odemeler.Count;
    }

    public record OdemeListeSatiri(OdemePlani Odeme, OdemeDurumu Durum, bool BuAyOdendi);
    public record TamamlananOdemeSatiri(OdemePlani Odeme, decimal ToplamOdenenTutar, DateTime? TamamlanmaTarihi, DateTime? SonOdemeHareketiTarihi);
}
