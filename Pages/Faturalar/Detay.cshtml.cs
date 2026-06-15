using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MuhasebeTakip2.App.Data;
using MuhasebeTakip2.App.Models;

namespace MuhasebeTakip2.App.Pages.Faturalar;

public class DetayModel : PageModel
{
    private readonly AppDbContext _db;

    public DetayModel(AppDbContext db)
    {
        _db = db;
    }

    public Fatura? Fatura { get; set; }
    public List<KasaHareket> Hareketler { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Fatura = await _db.Faturalar
            .Include(x => x.CariKart)
            .Include(x => x.Kalemler)
            .FirstOrDefaultAsync(x => x.Id == id && x.FirmaId == firmaId.Value);

        if (Fatura == null)
            return NotFound();

        Hareketler = await _db.KasaHareketleri
            .Where(x => x.FirmaId == firmaId.Value &&
                (x.FaturaId == id || x.Aciklama.Contains(Fatura.FaturaNo)))
            .OrderByDescending(x => x.Tarih)
            .ThenByDescending(x => x.Id)
            .ToListAsync();

        return Page();
    }
}