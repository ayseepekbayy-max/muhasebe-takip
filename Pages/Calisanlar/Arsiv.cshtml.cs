using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Calisanlar;

public class ArsivModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public ArsivModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
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

        var eskiDeger = IslemGecmisiSnapshots.Calisan(calisan);

        calisan.AktifMi = true;
        calisan.AyrilisTarihi = null;
        calisan.AyrilisNotu = null;

        await _islemGecmisi.KaydetAsync(
            "Çalışanlar",
            "Düzenleme",
            $"{calisan.AdSoyad} çalışanı arşivden aktif listeye alındı.",
            eskiDeger,
            IslemGecmisiSnapshots.Calisan(calisan));
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
