using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Musteriler.Detay;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public Musteri? Musteri { get; set; }
    public List<MusteriIs> Isler { get; set; } = new();

    public decimal ToplamGelir { get; set; }
    public decimal ToplamMasraf { get; set; }
    public decimal NetTutar => ToplamGelir - ToplamMasraf;

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    [BindProperty]
    public DateTime YeniIsTarih { get; set; } = DateTime.Today;

    [BindProperty]
    public string YeniIsAdi { get; set; } = "";

    [BindProperty]
    public decimal YeniIsGelir { get; set; }

    [BindProperty]
    public int MasrafIsId { get; set; }

    [BindProperty]
    public DateTime YeniMasrafTarih { get; set; } = DateTime.Today;

    [BindProperty]
    public string YeniMasrafAciklama { get; set; } = "";

    [BindProperty]
    public decimal YeniMasrafTutar { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);

        if (Musteri == null)
            return RedirectToPage("/Musteriler/Index");

        return Page();
    }

    public async Task<IActionResult> OnPostIsEkleAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);

        if (Musteri == null)
            return RedirectToPage("/Musteriler/Index");

        YeniIsAdi = (YeniIsAdi ?? "").Trim();

        if (string.IsNullOrWhiteSpace(YeniIsAdi))
        {
            Hata = "İş adı boş olamaz.";
            return Page();
        }

        if (YeniIsGelir < 0)
        {
            Hata = "Gelir 0'dan küçük olamaz.";
            return Page();
        }

        try
        {
            var kayitTarihi = DateTime.SpecifyKind(YeniIsTarih.Date, DateTimeKind.Utc);

            var yeniIs = new MusteriIs
            {
                FirmaId = firmaId.Value,
                MusteriId = id,
                Musteri = null,
                Firma = null,
                Ad = Musteri.AdSoyad ?? "",
                IsAdi = YeniIsAdi,
                Gelir = YeniIsGelir,
                Tarih = kayitTarihi
            };

            _db.MusteriIsler.Add(yeniIs);
            await _db.SaveChangesAsync();

            return RedirectToPage(new { id });
        }
        catch (Exception ex)
        {
            var detay = ex.InnerException?.Message ?? ex.Message;
            Hata = "İş eklenirken hata oluştu: " + detay;
            await YukleAsync(id, firmaId.Value);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostMasrafEkleAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);

        if (Musteri == null)
            return RedirectToPage("/Musteriler/Index");

        YeniMasrafAciklama = (YeniMasrafAciklama ?? "").Trim();

        if (MasrafIsId <= 0)
        {
            Hata = "Masraf eklenecek iş seçilemedi.";
            return Page();
        }

        if (YeniMasrafTutar <= 0)
        {
            Hata = "Masraf tutarı 0'dan büyük olmalı.";
            return Page();
        }

        var seciliIs = await _db.MusteriIsler
            .FirstOrDefaultAsync(x =>
                x.Id == MasrafIsId &&
                x.MusteriId == id &&
                x.FirmaId == firmaId.Value);

        if (seciliIs == null)
        {
            Hata = "Masraf eklenecek iş bulunamadı.";
            await YukleAsync(id, firmaId.Value);
            return Page();
        }

        try
        {
            var kayitTarihi = DateTime.SpecifyKind(YeniMasrafTarih.Date, DateTimeKind.Utc);

            var yeniMasraf = new MusteriMasraf
            {
                FirmaId = firmaId.Value,
                MusteriIsId = MasrafIsId,
                MusteriIs = null,
                Firma = null,
                Ad = Musteri.AdSoyad ?? "",
                Aciklama = YeniMasrafAciklama,
                Tutar = YeniMasrafTutar,
                Tarih = kayitTarihi
            };

            _db.MusteriMasraflar.Add(yeniMasraf);
            await _db.SaveChangesAsync();

            return RedirectToPage(new { id });
        }
        catch (Exception ex)
        {
            var detay = ex.InnerException?.Message ?? ex.Message;
            Hata = "Masraf eklenirken hata oluştu: " + detay;
            await YukleAsync(id, firmaId.Value);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostIsSilAsync(int id, int isId)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);

        if (Musteri == null)
            return RedirectToPage("/Musteriler/Index");

        try
        {
            var silinecekIs = await _db.MusteriIsler
                .Include(x => x.Masraflar)
                .FirstOrDefaultAsync(x =>
                    x.Id == isId &&
                    x.MusteriId == id &&
                    x.FirmaId == firmaId.Value);

            if (silinecekIs != null)
            {
                if (silinecekIs.Masraflar.Any())
                    _db.MusteriMasraflar.RemoveRange(silinecekIs.Masraflar);

                _db.MusteriIsler.Remove(silinecekIs);
                await _db.SaveChangesAsync();
            }

            return RedirectToPage(new { id });
        }
        catch (Exception ex)
        {
            var detay = ex.InnerException?.Message ?? ex.Message;
            Hata = "İş silinirken hata oluştu: " + detay;
            await YukleAsync(id, firmaId.Value);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostMasrafSilAsync(int id, int masrafId)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);

        if (Musteri == null)
            return RedirectToPage("/Musteriler/Index");

        try
        {
            var silinecekMasraf = await _db.MusteriMasraflar
                .Include(x => x.MusteriIs)
                .FirstOrDefaultAsync(x =>
                    x.Id == masrafId &&
                    x.FirmaId == firmaId.Value &&
                    x.MusteriIs != null &&
                    x.MusteriIs.MusteriId == id);

            if (silinecekMasraf != null)
            {
                _db.MusteriMasraflar.Remove(silinecekMasraf);
                await _db.SaveChangesAsync();
            }

            return RedirectToPage(new { id });
        }
        catch (Exception ex)
        {
            var detay = ex.InnerException?.Message ?? ex.Message;
            Hata = "Masraf silinirken hata oluştu: " + detay;
            await YukleAsync(id, firmaId.Value);
            return Page();
        }
    }

    private async Task YukleAsync(int id, int firmaId)
    {
        Musteri = await _db.Musteriler
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (Musteri == null)
            return;

        Isler = await _db.MusteriIsler
            .Include(x => x.Masraflar)
            .Where(x => x.MusteriId == id && x.FirmaId == firmaId)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        ToplamGelir = Isler.Sum(x => x.Gelir);
        ToplamMasraf = Isler.SelectMany(x => x.Masraflar).Sum(x => x.Tutar);
    }
}