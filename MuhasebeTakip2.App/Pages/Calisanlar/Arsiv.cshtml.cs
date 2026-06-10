using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Calisanlar;

public class ArsivModel : PageModel
{
    private readonly AppDbContext _db;

    public ArsivModel(AppDbContext db)
    {
        _db = db;
    }

    public List<Calisan> Liste { get; set; } = new();

    public string Mesaj { get; set; } = "";
    public string Hata { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(firmaId.Value);
        return Page();
    }

    public async Task<IActionResult> OnPostGeriAlAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var calisan = await _db.Calisanlar
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value && !x.AktifMi);

        if (calisan == null)
        {
            Hata = "Arşiv kaydı bulunamadı.";
            await YukleAsync(firmaId.Value);
            return Page();
        }

        calisan.AktifMi = true;
        calisan.AyrilisTarihi = null;
        calisan.AyrilisNotu = null;

        await _db.SaveChangesAsync();

        Mesaj = "Çalışan tekrar aktif listeye alındı.";
        await YukleAsync(firmaId.Value);
        return Page();
    }

    private async Task YukleAsync(int firmaId)
    {
        Liste = await _db.Calisanlar
            .Where(x => x.FirmaId == firmaId && !x.AktifMi)
            .OrderByDescending(x => x.AyrilisTarihi)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
    }
}