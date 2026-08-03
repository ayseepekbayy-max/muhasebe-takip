using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;
using MuhasebeTakip2.App.Services;

namespace MuhasebeTakip2.App.Pages.Cekler;

public class DetayModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly ICekDurumService _cekDurumService;

    public DetayModel(AppDbContext db, ICekDurumService cekDurumService)
    {
        _db = db;
        _cekDurumService = cekDurumService;
    }

    public Cek Cek { get; set; } = default!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null) return RedirectToPage("/Login");

        var cek = await _db.Cekler.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);
        if (cek == null) return NotFound();
        Cek = cek;
        return Page();
    }

    public async Task<IActionResult> OnPostDurumDegistirAsync(int id, bool odendiMi)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null) return RedirectToPage("/Login");

        var sonuc = await _cekDurumService.DurumDegistirAsync(firmaId.Value, id, odendiMi);
        TempData[sonuc.Basarili ? "Basari" : "Hata"] = sonuc.Mesaj;
        return RedirectToPage(new { id });
    }
}
