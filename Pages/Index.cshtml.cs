using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;

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

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        var bugun = DateTime.Today;

        BugunGiris = await _db.KasaHareketleri
            .Where(x =>
                x.FirmaId == firmaId.Value &&
                x.Tarih.Date == bugun &&
                x.Tip == Models.HareketTipi.Giris)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        BugunCikis = await _db.KasaHareketleri
            .Where(x =>
                x.FirmaId == firmaId.Value &&
                x.Tarih.Date == bugun &&
                x.Tip == Models.HareketTipi.Cikis)
            .SumAsync(x => (decimal?)x.Tutar) ?? 0;

        CariSayisi = await _db.CariKartlar
            .CountAsync(x => x.FirmaId == firmaId.Value);

        CalisanSayisi = await _db.Calisanlar
            .CountAsync(x => x.FirmaId == firmaId.Value);

        return Page();
    }
}