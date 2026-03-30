using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db)
    {
        _db = db;
    }

    public decimal BugunGiris { get; set; }
    public decimal BugunCikis { get; set; }
    public int CariSayisi { get; set; }
    public int CalisanSayisi { get; set; }

    public decimal ToplamGiris { get; set; }
    public decimal ToplamCikis { get; set; }
    public decimal KasaBakiye { get; set; }

    public List<KasaHareket> Hareketler { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var today = DateTime.Today;

        BugunGiris = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId.Value && x.Tarih == today && x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        BugunCikis = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId.Value && x.Tarih == today && x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        CariSayisi = await _db.CariKartlar
            .CountAsync(x => x.FirmaId == firmaId.Value);

        CalisanSayisi = await _db.Calisanlar
            .CountAsync(x => x.FirmaId == firmaId.Value);

        ToplamGiris = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId.Value && x.Tip == HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        ToplamCikis = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId.Value && x.Tip == HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        KasaBakiye = ToplamGiris - ToplamCikis;

        Hareketler = await _db.KasaHareketleri
            .Include(x => x.CariKart)
            .Where(x => x.FirmaId == firmaId.Value)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .Take(10)
            .ToListAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostSilHareketAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var h = await _db.KasaHareketleri
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);

        if (h != null)
        {
            _db.KasaHareketleri.Remove(h);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage("/Index");
    }
}