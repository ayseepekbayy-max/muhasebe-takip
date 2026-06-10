using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Kasa;

public class HareketDuzenleModel : PageModel
{
    private readonly AppDbContext _db;
    public HareketDuzenleModel(AppDbContext db) => _db = db;

    public List<SelectListItem> CariSecenekleri { get; set; } = new();

    [BindProperty]
    public KasaHareket Hareket { get; set; } = new();

    public string Hata { get; set; } = "";
    public string Mesaj { get; set; } = "";

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var h = await _db.KasaHareketleri
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (h == null)
            return NotFound();

        Hareket = h;

        await YukleCariSecenekleriAsync(firmaId.Value);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleCariSecenekleriAsync(firmaId.Value);

        if (Hareket.Tutar <= 0)
        {
            Hata = "Tutar 0'dan büyük olmalı.";
            return Page();
        }

        if (!Enum.IsDefined(typeof(HareketTipi), Hareket.Tip))
        {
            Hata = "Geçersiz hareket tipi.";
            return Page();
        }

        Hareket.Aciklama = (Hareket.Aciklama ?? "").Trim();

        if (Hareket.CariKartId == 0)
            Hareket.CariKartId = null;

        if (Hareket.CariKartId.HasValue)
        {
            var secilenCari = await _db.CariKartlar
                .AnyAsync(x => x.Id == Hareket.CariKartId.Value && x.FirmaId == firmaId);

            if (!secilenCari)
            {
                Hata = "Geçersiz cari seçimi.";
                return Page();
            }
        }

        var dbHareket = await _db.KasaHareketleri
            .FirstOrDefaultAsync(x => x.Id == Hareket.Id && x.FirmaId == firmaId);

        if (dbHareket == null)
            return NotFound();

        dbHareket.Tarih = Hareket.Tarih;
        dbHareket.Tip = Hareket.Tip;
        dbHareket.Tutar = Hareket.Tutar;
        dbHareket.Aciklama = Hareket.Aciklama;
        dbHareket.CariKartId = Hareket.CariKartId;

        await _db.SaveChangesAsync();
        Mesaj = "Kasa hareketi güncellendi.";

        return RedirectToPage("/Kasa/Hareketler");
    }

    private async Task YukleCariSecenekleriAsync(int firmaId)
    {
        CariSecenekleri = await _db.CariKartlar
            .Where(x => x.FirmaId == firmaId)
            .OrderBy(x => x.Unvan)
            .Select(x => new SelectListItem
            {
                Value = x.Id.ToString(),
                Text = $"{x.Unvan} ({x.Tip})"
            })
            .ToListAsync();

        CariSecenekleri.Insert(0, new SelectListItem
        {
            Value = "",
            Text = "Cari seç (opsiyonel)"
        });
    }
}