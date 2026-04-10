using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Stoklar.Detay;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public StokUrun? Urun { get; set; }
    public List<StokHareket> Hareketler { get; set; } = new();

    public decimal ToplamGiris { get; set; }
    public decimal ToplamCikis { get; set; }
    public decimal Stok => ToplamGiris - ToplamCikis;

    public string Hata { get; set; } = "";

    [BindProperty]
    public DateTime Tarih { get; set; } = DateTime.Today;

    [BindProperty]
    public decimal Miktar { get; set; }

    [BindProperty]
    public string? Aciklama { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        if (Urun == null)
            return NotFound();

        Tarih = DateTime.Today;
        Miktar = 0;
        Aciklama = "";

        return Page();
    }

    public async Task<IActionResult> OnPostGirisAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        if (Urun == null)
            return NotFound();

        if (Miktar <= 0)
        {
            ModelState.AddModelError("", "Miktar 0'dan büyük olmalı.");
            return Page();
        }

        try
        {
            _db.StokHareketleri.Add(new StokHareket
            {
                FirmaId = firmaId.Value,
                StokUrunId = id,
                Tarih = Tarih,
                Tip = StokHareketTipi.Giris,
                Miktar = Miktar,
                Aciklama = (Aciklama ?? "").Trim()
            });

            await _db.SaveChangesAsync();
            return RedirectToPage(new { id });
        }
        catch (Exception ex)
        {
            Hata = "Stok giriş kaydı eklenirken hata oluştu: " + ex.Message;
            await YukleAsync(id, firmaId.Value);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCikisAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        if (Urun == null)
            return NotFound();

        if (Miktar <= 0)
        {
            ModelState.AddModelError("", "Miktar 0'dan büyük olmalı.");
            return Page();
        }

        if (Stok < Miktar)
        {
            ModelState.AddModelError("", "Mevcut stoktan fazla çıkış yapılamaz.");
            return Page();
        }

        try
        {
            _db.StokHareketleri.Add(new StokHareket
            {
                FirmaId = firmaId.Value,
                StokUrunId = id,
                Tarih = Tarih,
                Tip = StokHareketTipi.Cikis,
                Miktar = Miktar,
                Aciklama = (Aciklama ?? "").Trim()
            });

            await _db.SaveChangesAsync();
            return RedirectToPage(new { id });
        }
        catch (Exception ex)
        {
            Hata = "Stok çıkış kaydı eklenirken hata oluştu: " + ex.Message;
            await YukleAsync(id, firmaId.Value);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSilAsync(int id, int id2)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        if (Urun == null)
            return NotFound();

        try
        {
            var h = await _db.StokHareketleri
                .FirstOrDefaultAsync(x => x.Id == id2 && x.StokUrunId == id && x.FirmaId == firmaId.Value);

            if (h != null)
            {
                _db.StokHareketleri.Remove(h);
                await _db.SaveChangesAsync();
            }

            return RedirectToPage(new { id });
        }
        catch (Exception ex)
        {
            Hata = "Hareket silinirken hata oluştu: " + ex.Message;
            await YukleAsync(id, firmaId.Value);
            return Page();
        }
    }

    private async Task YukleAsync(int id, int firmaId)
    {
        Urun = await _db.StokUrunler
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (Urun == null)
            return;

        Hareketler = await _db.StokHareketleri
            .Where(x => x.StokUrunId == id && x.FirmaId == firmaId)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();

        ToplamGiris = await _db.StokHareketleri
            .Where(x => x.StokUrunId == id && x.FirmaId == firmaId && x.Tip == StokHareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Miktar) ?? 0;

        ToplamCikis = await _db.StokHareketleri
            .Where(x => x.StokUrunId == id && x.FirmaId == firmaId && x.Tip == StokHareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Miktar) ?? 0;
    }
}