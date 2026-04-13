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

    public List<CalisanArsiv> Liste { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var firmaId = HttpContext.Session.GetInt32("FirmaId");
        if (firmaId == null)
            return RedirectToPage("/Login");

        Liste = await _db.CalisanArsivleri
            .Where(x => x.FirmaId == firmaId.Value)
            .OrderByDescending(x => x.AyrilisTarihi)
            .ToListAsync();

        return Page();
    }
}