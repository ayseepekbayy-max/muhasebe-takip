using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.CariKartlar.Detay;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public CariKart? Cari { get; set; }
    public List<KasaHareket> Hareketler { get; set; } = new();

    public decimal ToplamGiris { get; set; }
    public decimal ToplamCikis { get; set; }
    public decimal Bakiye => ToplamGiris - ToplamCikis;

    [BindProperty] public decimal Tutar { get; set; }
    [BindProperty] public string? Aciklama { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        await YukleAsync(id);
        if (Cari == null) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostTahsilatAsync(int id)
    {
        await YukleAsync(id);
        if (Cari == null) return NotFound();

        if (Tutar <= 0)
        {
            ModelState.AddModelError("", "Tutar 0'dan büyük olmalı.");
            return Page();
        }

        _db.KasaHareketleri.Add(new KasaHareket
        {
            Tarih = DateTime.Today,
            Tip = HareketTipi.Giris,
            Tutar = Tutar,
            Aciklama = (Aciklama ?? "").Trim(),
            CariKartId = id
        });

        await _db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostOdemeAsync(int id)
    {
        await YukleAsync(id);
        if (Cari == null) return NotFound();

        if (Tutar <= 0)
        {
            ModelState.AddModelError("", "Tutar 0'dan büyük olmalı.");
            return Page();
        }

        _db.KasaHareketleri.Add(new KasaHareket
        {
            Tarih = DateTime.Today,
            Tip = HareketTipi.Cikis,
            Tutar = Tutar,
            Aciklama = (Aciklama ?? "").Trim(),
            CariKartId = id
        });

        await _db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }

    private async Task YukleAsync(int id)
    {
        Cari = await _db.CariKartlar.FirstOrDefaultAsync(x => x.Id == id);
        if (Cari == null) return;

        Hareketler = await _db.KasaHareketleri
            .Where(x => x.CariKartId == id)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .Take(50)
            .ToListAsync();

        ToplamGiris = await _db.KasaHareketleri
            .Where(x => x.CariKartId == id && x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        ToplamCikis = await _db.KasaHareketleri
            .Where(x => x.CariKartId == id && x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;
    }
}