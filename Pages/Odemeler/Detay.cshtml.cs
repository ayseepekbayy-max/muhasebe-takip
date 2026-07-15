using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Odemeler;

public class DetayModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IIslemGecmisiService _islemGecmisi;

    public DetayModel(AppDbContext db, IIslemGecmisiService islemGecmisi)
    {
        _db = db;
        _islemGecmisi = islemGecmisi;
    }

    public OdemePlani? Odeme { get; set; }
    public List<OdemeHareketi> Hareketler { get; set; } = new();
    public OdemeDurumu Durum { get; set; }
    public bool BuAyOdendi { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        await YukleAsync(id, firmaId.Value);
        if (Odeme == null)
            return NotFound();

        return Page();
    }


    private async Task YukleAsync(int id, int firmaId)
    {
        Odeme = await _db.OdemePlanlari
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId);

        if (Odeme == null)
            return;

        Hareketler = await _db.OdemeHareketleri
            .AsNoTracking()
            .Where(x => x.FirmaId == firmaId && x.OdemePlaniId == id)
            .OrderByDescending(x => x.OdemeTarihi)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        var bugun = DateTime.UtcNow.Date;
        var ayBaslangic = new DateTime(bugun.Year, bugun.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        BuAyOdendi = Hareketler.Any(x => x.OdemeTarihi >= ayBaslangic && x.OdemeTarihi < ayBaslangic.AddMonths(1));
        Durum = OdemePlanlamaService.Durum(Odeme, bugun, BuAyOdendi);
    }
}