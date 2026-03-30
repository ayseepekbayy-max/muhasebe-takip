using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Musteriler.Detay;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public Musteri? Musteri { get; set; }
    public List<MusteriIs> Isler { get; set; } = new();

    public decimal ToplamGelir { get; set; }
    public decimal ToplamMasraf { get; set; }
    public decimal ToplamKar => ToplamGelir - ToplamMasraf;

    // İş bazlı hesaplar
    public Dictionary<int, decimal> IsMasrafToplamlari { get; set; } = new();
    public Dictionary<int, decimal> IsKarToplamlari { get; set; } = new();

    // İş ekleme formu
    [BindProperty] public DateTime IsTarih { get; set; } = DateTime.Today;
    [BindProperty] public string? IsAdi { get; set; }
    [BindProperty] public decimal IsGelir { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await YukleAsync(id);
        if (Musteri == null) return NotFound();

        IsTarih = DateTime.Today;
        IsAdi = "";
        IsGelir = 0;

        return Page();
    }

    public async Task<IActionResult> OnPostIsEkleAsync(int id)
    {
        await YukleAsync(id);
        if (Musteri == null) return NotFound();

        var ad = (IsAdi ?? "").Trim();
        if (string.IsNullOrWhiteSpace(ad))
        {
            ModelState.AddModelError("", "İş adı boş olamaz.");
            return Page();
        }
        if (IsGelir < 0)
        {
            ModelState.AddModelError("", "Gelir 0'dan küçük olamaz.");
            return Page();
        }

        _db.MusteriIsler.Add(new MusteriIs
        {
            MusteriId = id,
            Tarih = IsTarih,
            IsAdi = ad,
            Gelir = IsGelir
        });

        await _db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMasrafEkleAsync(
        int id,
        int isId,
        DateTime MasrafTarih,
        string? MasrafAciklama,
        decimal MasrafTutar)
    {
        // müşteri var mı
        var musteriVar = await _db.Musteriler.AnyAsync(x => x.Id == id);
        if (!musteriVar) return NotFound();

        // iş bu müşteriye ait mi
        var isVar = await _db.MusteriIsler.AnyAsync(x => x.Id == isId && x.MusteriId == id);
        if (!isVar) return NotFound();

        if (MasrafTutar <= 0)
        {
            ModelState.AddModelError("", "Masraf tutarı 0'dan büyük olmalı.");
            await YukleAsync(id);
            return Page();
        }

        _db.MusteriMasraflar.Add(new MusteriMasraf
        {
            MusteriIsId = isId,
            Tarih = MasrafTarih,
            Aciklama = (MasrafAciklama ?? "").Trim(),
            Tutar = MasrafTutar
        });

        await _db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMasrafSilAsync(int id, int isId, int masrafId)
    {
        var masraf = await _db.MusteriMasraflar
            .Include(x => x.MusteriIs)
            .FirstOrDefaultAsync(x => x.Id == masrafId);

        if (masraf != null && masraf.MusteriIs != null && masraf.MusteriIs.MusteriId == id && masraf.MusteriIsId == isId)
        {
            _db.MusteriMasraflar.Remove(masraf);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage(new { id });
    }

    private async Task YukleAsync(int id)
    {
        Musteri = await _db.Musteriler.FirstOrDefaultAsync(x => x.Id == id);
        if (Musteri == null) return;

        Isler = await _db.MusteriIsler
            .Where(x => x.MusteriId == id)
            .Include(x => x.Masraflar)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        ToplamGelir = Isler.Sum(x => x.Gelir);
        ToplamMasraf = Isler.Sum(x => x.Masraflar.Sum(m => m.Tutar));

        IsMasrafToplamlari = Isler.ToDictionary(
            x => x.Id,
            x => x.Masraflar.Sum(m => m.Tutar)
        );

        IsKarToplamlari = Isler.ToDictionary(
            x => x.Id,
            x => x.Gelir - x.Masraflar.Sum(m => m.Tutar)
        );
    }
}