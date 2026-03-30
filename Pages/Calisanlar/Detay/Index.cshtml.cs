using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Calisanlar.Detay;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public Calisan? Calisan { get; set; }

    public List<CalisanAvans> Kayitlar { get; set; } = new();

    public decimal ToplamMaas { get; set; }
    public decimal ToplamAvans { get; set; }
    public decimal Kalan => ToplamMaas - ToplamAvans;

    [BindProperty]
    public DateTime Tarih { get; set; } = DateTime.Today;

    [BindProperty]
    public decimal Tutar { get; set; }

    [BindProperty]
    public string? Aciklama { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
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

        await YukleAsync(id, firmaId.Value);
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
            Tip = CalisanHareketTipi.Avans
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

        await YukleAsync(id, firmaId.Value);
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
            Tip = CalisanHareketTipi.MaasOdeme
        };

        _db.CalisanAvanslari.Add(kayit);
        await _db.SaveChangesAsync();

        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostSilAsync(int id, int id2)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var kayit = await _db.CalisanAvanslari
            .FirstOrDefaultAsync(x => x.Id == id2 && x.CalisanId == id && x.FirmaId == firmaId.Value);

        if (kayit != null)
        {
            _db.CalisanAvanslari.Remove(kayit);
            await _db.SaveChangesAsync();
        }

        return RedirectToPage(new { id });
    }

    private async Task YukleAsync(int id, int firmaId)
    {
        Calisan = await _db.Calisanlar
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (Calisan == null)
            return;

        Kayitlar = await _db.CalisanAvanslari
            .Where(x => x.CalisanId == id && x.FirmaId == firmaId)
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .Take(200)
            .ToListAsync();

        ToplamAvans = await _db.CalisanAvanslari
            .Where(x => x.CalisanId == id && x.FirmaId == firmaId && x.Tip == CalisanHareketTipi.Avans)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        ToplamMaas = await _db.CalisanAvanslari
            .Where(x => x.CalisanId == id && x.FirmaId == firmaId && x.Tip == CalisanHareketTipi.MaasOdeme)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;
    }
}